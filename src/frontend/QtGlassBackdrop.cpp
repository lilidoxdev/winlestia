// C++20 / QtGlassBackdrop.cpp / Windows 11, MSVC. Per-pixel composited blur with bounded native hosts.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <dwmapi.h>
#include <DispatcherQueue.h>
#include <windows.ui.composition.interop.h>
#include <windows.graphics.effects.interop.h>
#include <windows.graphics.interop.h>
#include <winrt/Windows.Foundation.Numerics.h>
#include <winrt/Windows.System.h>
#include <winrt/Windows.UI.Composition.h>
#include <winrt/Windows.UI.Composition.Desktop.h>
#include <winrt/Windows.Graphics.h>
#include <winrt/Windows.Graphics.Effects.h>
#include <d2d1.h>
#include <d2d1effects.h>

#include <unordered_map>
#include <memory>
#include <vector>
#include <algorithm>
#include <cmath>
#include <utility>
using namespace winrt;
using namespace Windows::UI::Composition;
using namespace Windows::UI::Composition::Desktop;
using Windows::Foundation::Numerics::float2;
// Minimal Direct2D effect metadata for the compositor; no Win2D runtime dependency.
struct FrostedBlur : winrt::implements<FrostedBlur, Windows::Graphics::Effects::IGraphicsEffect, Windows::Graphics::Effects::IGraphicsEffectSource, ABI::Windows::Graphics::Effects::IGraphicsEffectD2D1Interop> {
    Windows::Graphics::Effects::IGraphicsEffectSource source{nullptr};
    winrt::hstring Name(){return L"FrostedGlass";} void Name(winrt::hstring const&){}
    HRESULT __stdcall GetEffectId(GUID* value) noexcept final {if(!value)return E_POINTER;*value=CLSID_D2D1GaussianBlur;return S_OK;}
    HRESULT __stdcall GetNamedPropertyMapping(LPCWSTR,UINT*,ABI::Windows::Graphics::Effects::GRAPHICS_EFFECT_PROPERTY_MAPPING*) noexcept final {return E_INVALIDARG;}
    HRESULT __stdcall GetPropertyCount(UINT* value) noexcept final {if(!value)return E_POINTER;*value=3;return S_OK;}
    HRESULT __stdcall GetProperty(UINT index,ABI::Windows::Foundation::IPropertyValue** value) noexcept final {try{
        if(!value)return E_POINTER;*value=nullptr;
        Windows::Foundation::IPropertyValue property{nullptr};
        switch(index){case 0:property=Windows::Foundation::PropertyValue::CreateSingle(8.0f).as<Windows::Foundation::IPropertyValue>();break;case 1:property=Windows::Foundation::PropertyValue::CreateUInt32(D2D1_GAUSSIANBLUR_OPTIMIZATION_BALANCED).as<Windows::Foundation::IPropertyValue>();break;case 2:property=Windows::Foundation::PropertyValue::CreateUInt32(D2D1_BORDER_MODE_HARD).as<Windows::Foundation::IPropertyValue>();break;default:return E_INVALIDARG;}
        *value=static_cast<ABI::Windows::Foundation::IPropertyValue*>(winrt::detach_abi(property));return S_OK;
    }catch(...){return winrt::to_hresult();}}
    HRESULT __stdcall GetSourceCount(UINT* value) noexcept final {if(!value)return E_POINTER;*value=1;return S_OK;}
    HRESULT __stdcall GetSource(UINT index,ABI::Windows::Graphics::Effects::IGraphicsEffectSource** value) noexcept final {try{if(!value)return E_POINTER;*value=nullptr;if(index!=0)return E_INVALIDARG;source.as<ABI::Windows::Graphics::Effects::IGraphicsEffectSource>().copy_to(value);return S_OK;}catch(...){return winrt::to_hresult();}}
};

struct GeometrySource : winrt::implements<GeometrySource, Windows::Graphics::IGeometrySource2D, ABI::Windows::Graphics::IGeometrySource2DInterop> {
    winrt::com_ptr<ID2D1Geometry> geometry;
    HRESULT __stdcall GetGeometry(ID2D1Geometry** value) noexcept final {if(!value)return E_POINTER;geometry.copy_to(value);return S_OK;}
    HRESULT __stdcall TryGetGeometryUsingFactory(ID2D1Factory*,ID2D1Geometry** value) noexcept final {if(!value)return E_POINTER;*value=nullptr;return S_OK;}
};
struct CompositionContext {
    Windows::System::DispatcherQueueController queue{nullptr};Compositor compositor{nullptr};winrt::com_ptr<ID2D1Factory> factory;
};
static CompositionContext* composition=nullptr;
static void InitializeComposition(){
    if(composition)return;auto value=std::make_unique<CompositionContext>();
    if(!Windows::System::DispatcherQueue::GetForCurrentThread()){DispatcherQueueOptions options{sizeof(options),DQTYPE_THREAD_CURRENT,DQTAT_COM_STA};check_hresult(CreateDispatcherQueueController(options,reinterpret_cast<ABI::Windows::System::IDispatcherQueueController**>(put_abi(value->queue))));}
    value->compositor=Compositor();check_hresult(D2D1CreateFactory(D2D1_FACTORY_TYPE_MULTI_THREADED,value->factory.put()));composition=value.release();
}
struct Host {
    HWND window{};RECT local{};int cell{-1};bool active{true},regionReady{};DesktopWindowTarget target{nullptr};SpriteVisual visual{nullptr};
    Host()=default;Host(const Host&)=delete;Host& operator=(const Host&)=delete;
    Host(Host&& other) noexcept:window(std::exchange(other.window,nullptr)),local(other.local),cell(other.cell),active(other.active),regionReady(other.regionReady),target(std::move(other.target)),visual(std::move(other.visual)){}
    ~Host(){if(target)try{target.Root(nullptr);target.Close();}catch(...){}if(window)DestroyWindow(window);}
};
struct Backdrop {HWND content{},owner{};std::vector<Host> hosts;int width{},height{},x{},y{};bool configured{},visible{},topmost{};float material{1};};
static std::unordered_map<HWND,std::unique_ptr<Backdrop>> windows;
static void Hide(Backdrop& item){for(auto& host:item.hosts)ShowWindow(host.window,SW_HIDE);item.visible=false;}
static void Clear(Backdrop& item){Hide(item);item.hosts.clear();item.configured=false;}
static LRESULT CALLBACK FrameProc(HWND hwnd,UINT message,WPARAM wp,LPARAM lp){
    if(message==WM_NCHITTEST)return HTTRANSPARENT;
    if(message==WM_ERASEBKGND)return 1;
    if(message==WM_TIMER){auto content=reinterpret_cast<HWND>(GetWindowLongPtrW(hwnd,GWLP_USERDATA));if(!IsWindow(content)||!IsWindowVisible(content))ShowWindow(hwnd,SW_HIDE);return 0;}
    return DefWindowProcW(hwnd,message,wp,lp);
}
static HRESULT CreateHost(Backdrop& item,RECT local){
    WNDCLASSW cls{};cls.lpfnWndProc=FrameProc;cls.hInstance=GetModuleHandleW(nullptr);cls.lpszClassName=L"WinLestiaContinuousGlass91";RegisterClassW(&cls);
    HWND host=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_TRANSPARENT|WS_EX_NOREDIRECTIONBITMAP|WS_EX_LAYERED,cls.lpszClassName,L"",WS_POPUP,0,0,1,1,item.owner,nullptr,cls.hInstance,nullptr);
    if(!host)return HRESULT_FROM_WIN32(GetLastError());
    // WS_EX_TRANSPARENT alone does not skip hit testing across application threads.
    // Layered + transparent makes the visual-only host ignore mouse input globally.
    if(!SetLayeredWindowAttributes(host,0,255,LWA_ALPHA)){DWORD error=GetLastError();DestroyWindow(host);return HRESULT_FROM_WIN32(error);}
    Host piece;piece.window=host;piece.local=local;
    // Empty and hidden until an accepted region and bounded geometry are installed.
    HRGN empty=CreateRectRgn(0,0,0,0);
    if(!empty||!SetWindowRgn(host,empty,FALSE)){if(empty)DeleteObject(empty);return E_FAIL;}
    SetWindowLongPtrW(host,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(item.content));SetTimer(host,1,100,nullptr);EnableWindow(host,FALSE);
    BOOL disabled=TRUE;DwmSetWindowAttribute(host,DWMWA_TRANSITIONS_FORCEDISABLED,&disabled,sizeof(disabled));
    int corner=1;DwmSetWindowAttribute(host,static_cast<DWMWINDOWATTRIBUTE>(33),&corner,sizeof(corner));DWORD border=0xfffffffe;DwmSetWindowAttribute(host,static_cast<DWMWINDOWATTRIBUTE>(34),&border,sizeof(border));
    InitializeComposition();BOOL enabled=TRUE;check_hresult(DwmSetWindowAttribute(host,static_cast<DWMWINDOWATTRIBUTE>(17),&enabled,sizeof(enabled)));
    auto interop=composition->compositor.as<ABI::Windows::UI::Composition::Desktop::ICompositorDesktopInterop>();
    check_hresult(interop->CreateDesktopWindowTarget(host,FALSE,reinterpret_cast<ABI::Windows::UI::Composition::Desktop::IDesktopWindowTarget**>(put_abi(piece.target))));
    piece.visual=composition->compositor.CreateSpriteVisual();piece.visual.Size(float2{static_cast<float>(local.right-local.left),static_cast<float>(local.bottom-local.top)});
    auto blur=winrt::make_self<FrostedBlur>();blur->source=CompositionEffectSourceParameter(L"Backdrop");
    auto effect=composition->compositor.CreateEffectFactory(blur.as<Windows::Graphics::Effects::IGraphicsEffect>()).CreateBrush();
    effect.SetSourceParameter(L"Backdrop",composition->compositor.CreateHostBackdropBrush());piece.visual.Brush(effect);piece.visual.Opacity(item.material);piece.target.Root(piece.visual);
    item.hosts.push_back(std::move(piece));return S_OK;
}
static HRESULT CopyRegion(Host& host,HRGN region){try{
    DWORD size=GetRegionData(region,0,nullptr);if(!size)return E_INVALIDARG;
    std::vector<BYTE> storage(size);auto data=reinterpret_cast<RGNDATA*>(storage.data());if(!GetRegionData(region,size,data))return E_FAIL;
    winrt::com_ptr<ID2D1PathGeometry> geometry;check_hresult(composition->factory->CreatePathGeometry(geometry.put()));winrt::com_ptr<ID2D1GeometrySink> sink;check_hresult(geometry->Open(sink.put()));sink->SetFillMode(D2D1_FILL_MODE_WINDING);
    auto rectangles=reinterpret_cast<const RECT*>(data->Buffer);
    for(DWORD i=0;i<data->rdh.nCount;i++){auto r=rectangles[i];sink->BeginFigure(D2D1::Point2F(static_cast<float>(r.left),static_cast<float>(r.top)),D2D1_FIGURE_BEGIN_FILLED);D2D1_POINT_2F points[]={{static_cast<float>(r.right),static_cast<float>(r.top)},{static_cast<float>(r.right),static_cast<float>(r.bottom)},{static_cast<float>(r.left),static_cast<float>(r.bottom)}};sink->AddLines(points,3);sink->EndFigure(D2D1_FIGURE_END_CLOSED);}
    check_hresult(sink->Close());auto source=winrt::make_self<GeometrySource>();source->geometry=geometry.as<ID2D1Geometry>();auto clipGeometry=composition->compositor.CreatePathGeometry();clipGeometry.Path(CompositionPath(source.as<Windows::Graphics::IGeometrySource2D>()));host.visual.Clip(composition->compositor.CreateGeometricClip(clipGeometry));host.visual.Size(float2{static_cast<float>(host.local.right-host.local.left),static_cast<float>(host.local.bottom-host.local.top)});
    HRGN copy=CreateRectRgn(0,0,0,0);if(!copy)return E_OUTOFMEMORY;if(CombineRgn(copy,region,nullptr,RGN_COPY)==ERROR){DeleteObject(copy);return E_FAIL;}if(!SetWindowRgn(host.window,copy,TRUE)){DeleteObject(copy);return E_FAIL;}return S_OK;
}catch(...){return winrt::to_hresult();}}
extern "C" __declspec(dllexport) HRESULT __stdcall BackdropAttach(HWND content){try{
    if(!IsWindow(content))return E_INVALIDARG;if(windows.contains(content))return S_OK;
    auto item=std::make_unique<Backdrop>();item->content=content;item->owner=GetWindow(content,GW_OWNER);windows.emplace(content,std::move(item));return S_OK;
}catch(...){return E_OUTOFMEMORY;}}
extern "C" __declspec(dllexport) HRESULT __stdcall BackdropRegion(HWND content,HRGN region){try{
    auto found=windows.find(content);if(found==windows.end())return E_INVALIDARG;auto& item=*found->second;Hide(item);item.configured=false;
    RECT rect{},box{};if(!region||!GetWindowRect(content,&rect)||GetRgnBox(region,&box)<=NULLREGION)return E_INVALIDARG;
    int w=rect.right-rect.left,h=rect.bottom-rect.top;if(w<1||h<1)return E_INVALIDARG;
    MONITORINFO monitor{sizeof(monitor)};GetMonitorInfoW(MonitorFromWindow(content,MONITOR_DEFAULTTONEAREST),&monitor);
    // A screen-sized Qt frame must use the dedicated eight-piece API.
    if(w>=(monitor.rcMonitor.right-monitor.rcMonitor.left)*.95&&h>=(monitor.rcMonitor.bottom-monitor.rcMonitor.top)*.95)return E_INVALIDARG;
    HRGN clip=CreateRectRgn(0,0,w,h);if(!clip)return E_OUTOFMEMORY;int type=CombineRgn(clip,clip,region,RGN_AND);
    if(type<=NULLREGION){DeleteObject(clip);return E_INVALIDARG;}
    if(item.hosts.size()!=1){Clear(item);HRESULT result=CreateHost(item,{0,0,w,h});if(FAILED(result)){DeleteObject(clip);return result;}}
    item.hosts[0].local={0,0,w,h};HRESULT result=CopyRegion(item.hosts[0],clip);DeleteObject(clip);
    if(FAILED(result)){Clear(item);return result;}item.width=w;item.height=h;item.configured=true;return S_OK;
}catch(...){auto found=windows.find(content);if(found!=windows.end())Clear(*found->second);return E_FAIL;}}
extern "C" __declspec(dllexport) HRESULT __stdcall BackdropFrame(HWND content,int thickness,int rounding){try{
    auto found=windows.find(content);if(found==windows.end())return E_INVALIDARG;auto& item=*found->second;Hide(item);item.configured=false;
    RECT rect{};if(!GetWindowRect(content,&rect))return E_INVALIDARG;int w=rect.right-rect.left,h=rect.bottom-rect.top,t=thickness,r=rounding;
    if(t<1||t>128||r<0||r>256||w<=2*(t+r)||h<=2*(t+r))return E_INVALIDARG;
    // Exclusive ring zones. Each native host is grown inwards well past the blur
    // kernel so the hard edge of its backdrop never lands inside the visible strip.
    RECT pieces[8]={{0,0,w,t},{0,h-t,w,h},{0,t,t,h-t},{w-t,t,w,h-t},{t,t,t+r,t+r},{w-t-r,t,w-t,t+r},{t,h-t-r,t+r,h-t},{w-t-r,h-t-r,w-t,h-t}};
    int pad=max(56,2*t+r);
    auto grow=[&](RECT piece){RECT box{max(0,piece.left-pad),max(0,piece.top-pad),min(w,piece.right+pad),min(h,piece.bottom+pad)};return box;};
    HRGN ring=CreateRectRgn(0,0,w,h),inner=CreateRoundRectRgn(t,t,w-t+1,h-t+1,2*r,2*r);
    if(!ring||!inner){if(ring)DeleteObject(ring);if(inner)DeleteObject(inner);return E_OUTOFMEMORY;}
    int result=CombineRgn(ring,ring,inner,RGN_DIFF);DeleteObject(inner);if(result<=NULLREGION){DeleteObject(ring);return E_FAIL;}
    if(item.hosts.size()!=8){Clear(item);for(auto piece:pieces){HRESULT hr=CreateHost(item,grow(piece));if(FAILED(hr)){DeleteObject(ring);Clear(item);return hr;}}}
    HRESULT status=S_OK;
    for(size_t i=0;i<8;i++){
        RECT zone=pieces[i],local=grow(zone);HRGN part=CreateRectRgn(zone.left,zone.top,zone.right,zone.bottom);
        if(!part){status=E_OUTOFMEMORY;break;}
        int type=CombineRgn(part,part,ring,RGN_AND);OffsetRgn(part,-local.left,-local.top);
        if(type==ERROR){DeleteObject(part);status=E_FAIL;break;}
        item.hosts[i].local=local;status=CopyRegion(item.hosts[i],part);DeleteObject(part);if(FAILED(status))break;
    }
    DeleteObject(ring);if(FAILED(status)){Clear(item);return status;}
    item.width=w;item.height=h;item.configured=true;return S_OK;
}catch(...){auto found=windows.find(content);if(found!=windows.end())Clear(*found->second);return E_FAIL;}}
extern "C" __declspec(dllexport) HRESULT __stdcall BackdropUpdate(HWND content,double,BOOL visible,BOOL topmost){
    auto found=windows.find(content);if(found==windows.end())return E_INVALIDARG;auto& item=*found->second;
    if(!visible||!IsWindow(content)||!IsWindowVisible(content)){Hide(item);return S_OK;}
    RECT rect{};if(!item.configured||!GetWindowRect(content,&rect)||rect.right-rect.left!=item.width||rect.bottom-rect.top!=item.height){Hide(item);return E_PENDING;}
    bool move=!item.visible||std::any_of(item.hosts.begin(),item.hosts.end(),[](const Host& host){return host.active&&!IsWindowVisible(host.window);})||item.x!=rect.left||item.y!=rect.top||item.topmost!=(topmost!=FALSE);
    if(move){
        for(auto& host:item.hosts){if(!host.active)continue;auto b=host.local;if(!SetWindowPos(host.window,topmost?HWND_TOPMOST:HWND_NOTOPMOST,rect.left+b.left,rect.top+b.top,b.right-b.left,b.bottom-b.top,SWP_NOACTIVATE|SWP_SHOWWINDOW)){Hide(item);return E_FAIL;}}
        SetWindowPos(content,topmost?HWND_TOPMOST:HWND_TOP,0,0,0,0,SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE);
        item.x=rect.left;item.y=rect.top;item.topmost=topmost!=FALSE;
    }
    item.visible=true;return S_OK;
}
extern "C" __declspec(dllexport) HWND __stdcall BackdropHandle(HWND content){auto found=windows.find(content);return found==windows.end()||found->second->hosts.empty()?nullptr:found->second->hosts[0].window;}
extern "C" __declspec(dllexport) HWND __stdcall BackdropPart(HWND content,int index){auto found=windows.find(content);return found==windows.end()||index<0||static_cast<size_t>(index)>=found->second->hosts.size()?nullptr:found->second->hosts[index].window;}
extern "C" __declspec(dllexport) void __stdcall BackdropDetach(HWND content){auto found=windows.find(content);if(found==windows.end())return;Clear(*found->second);windows.erase(found);}

// Shared logical material opacity: detail transmission after both layers is 1-opacity.
extern "C" __declspec(dllexport) HRESULT __stdcall BackdropMaterial(HWND content,float opacity){
    auto found=windows.find(content);if(found==windows.end()||!std::isfinite(opacity))return E_INVALIDARG;
    auto& item=*found->second;item.material=std::clamp(opacity,0.0f,1.0f)*.25f;
    for(auto& host:item.hosts)host.visual.Opacity(item.material);return S_OK;
}
struct SurfaceCommand {UINT32 kind;float points[6];};
static HRESULT SurfaceUpdate(HWND content,const SurfaceCommand* commands,UINT32 count,float dl,float dt,float dr,float db){try{
    auto found=windows.find(content);if(found==windows.end())return E_INVALIDARG;auto& item=*found->second;
    if(!commands||count<2||count>100000){Hide(item);return E_INVALIDARG;}
    RECT rect{};if(!GetWindowRect(content,&rect))return E_INVALIDARG;int width=rect.right-rect.left,height=rect.bottom-rect.top;
    if(width<1||height<1||width>20000||height>20000){Hide(item);return E_INVALIDARG;}
    InitializeComposition();auto& context=*composition;
    winrt::com_ptr<ID2D1PathGeometry> whole;check_hresult(context.factory->CreatePathGeometry(whole.put()));
    winrt::com_ptr<ID2D1GeometrySink> sink;check_hresult(whole->Open(sink.put()));sink->SetFillMode(D2D1_FILL_MODE_WINDING);bool open=false;
    for(UINT32 i=0;i<count;i++){
        auto c=commands[i];for(float v:c.points)if(!std::isfinite(v)){Hide(item);return E_INVALIDARG;}
        if(c.kind==0){if(open)sink->EndFigure(D2D1_FIGURE_END_CLOSED);sink->BeginFigure({c.points[0],c.points[1]},D2D1_FIGURE_BEGIN_FILLED);open=true;}
        else if(c.kind==1&&open)sink->AddLine({c.points[0],c.points[1]});
        else if(c.kind==2&&open)sink->AddBezier({{c.points[0],c.points[1]},{c.points[2],c.points[3]},{c.points[4],c.points[5]}});
        else {Hide(item);return E_INVALIDARG;}
    }
    if(open)sink->EndFigure(D2D1_FIGURE_END_CLOSED);check_hresult(sink->Close());
    constexpr int size=512,padding=40;int columns=(width+size-1)/size,rows=(height+size-1)/size;
    bool layout=item.width!=width||item.height!=height||item.hosts.empty(),activityChanged=layout;
    auto boundsFor=[&](int x,int y){return RECT{(std::max)(0,x-padding),(std::max)(0,y-padding),(std::min)(width,x+size+padding),(std::min)(height,y+size+padding)};};
    if(layout){
        Clear(item);dl=dt=0;dr=static_cast<float>(width);db=static_cast<float>(height);
        if(columns*rows<=64)for(int y=0;y<height;y+=size)for(int x=0;x<width;x+=size){check_hresult(CreateHost(item,boundsFor(x,y)));item.hosts.back().cell=y/size*columns+x/size;item.hosts.back().active=false;}
    }
    for(int y=0;y<height;y+=size)for(int x=0;x<width;x+=size){
        if(x+size<dl||x>dr||y+size<dt||y>db)continue;
        int id=y/size*columns+x/size;
        auto existing=std::find_if(item.hosts.begin(),item.hosts.end(),[&](const Host& host){return host.cell==id;});
        winrt::com_ptr<ID2D1RectangleGeometry> cell;check_hresult(context.factory->CreateRectangleGeometry(D2D1::RectF(static_cast<float>(x),static_cast<float>(y),static_cast<float>((std::min)(width,x+size)),static_cast<float>((std::min)(height,y+size))),cell.put()));
        winrt::com_ptr<ID2D1PathGeometry> part;check_hresult(context.factory->CreatePathGeometry(part.put()));winrt::com_ptr<ID2D1GeometrySink> output;check_hresult(part->Open(output.put()));
        check_hresult(whole->CombineWithGeometry(cell.get(),D2D1_COMBINE_MODE_INTERSECT,nullptr,.1f,output.get()));check_hresult(output->Close());
        D2D1_RECT_F area{};check_hresult(part->GetBounds(nullptr,&area));
        if(area.right<=area.left||area.bottom<=area.top){if(existing!=item.hosts.end()&&existing->active){existing->active=false;ShowWindow(existing->window,SW_HIDE);}continue;}
        if(existing==item.hosts.end()){check_hresult(CreateHost(item,boundsFor(x,y)));item.hosts.back().cell=id;existing=std::prev(item.hosts.end());existing->active=false;}
        auto& host=*existing;activityChanged|=!host.active;host.active=true;
        winrt::com_ptr<ID2D1TransformedGeometry> shifted;check_hresult(context.factory->CreateTransformedGeometry(part.get(),D2D1::Matrix3x2F::Translation(static_cast<float>(-host.local.left),static_cast<float>(-host.local.top)),shifted.put()));
        auto source=winrt::make_self<GeometrySource>();source->geometry=shifted.as<ID2D1Geometry>();
        auto geometry=context.compositor.CreatePathGeometry();geometry.Path(CompositionPath(source.as<Windows::Graphics::IGeometrySource2D>()));host.visual.Clip(context.compositor.CreateGeometricClip(geometry));
        if(!host.regionReady){
            HRGN region=CreateRectRgn(0,0,host.local.right-host.local.left,host.local.bottom-host.local.top);
            if(!region||!SetWindowRgn(host.window,region,TRUE)){if(region)DeleteObject(region);Clear(item);return E_FAIL;}host.regionReady=true;
        }
    }
    item.width=width;item.height=height;item.configured=true;if(activityChanged)item.visible=false;return S_OK;
}catch(...){auto found=windows.find(content);if(found!=windows.end())Hide(*found->second);return winrt::to_hresult();}}
extern "C" __declspec(dllexport) HRESULT __stdcall BackdropSurface(HWND content,const SurfaceCommand* commands,UINT32 count){return SurfaceUpdate(content,commands,count,0,0,20000,20000);}
extern "C" __declspec(dllexport) HRESULT __stdcall BackdropSurfaceDirty(HWND content,const SurfaceCommand* commands,UINT32 count,float left,float top,float right,float bottom){return SurfaceUpdate(content,commands,count,left,top,right,bottom);}
