// language: C++20, file: GlassBackdrop.cpp, target: Windows 11 / MSVC x64, compositor-owned clipped backdrop under a per-pixel WPF surface
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <dwmapi.h>
#include <DispatcherQueue.h>
#include <windows.ui.composition.interop.h>
#include <winrt/Windows.Foundation.Numerics.h>
#include <winrt/Windows.System.h>
#include <winrt/Windows.UI.Composition.h>
#include <winrt/Windows.UI.Composition.Desktop.h>
#include <unordered_map>
#include <memory>

using namespace winrt;
using namespace Windows::UI::Composition;
using namespace Windows::UI::Composition::Desktop;
using Windows::Foundation::Numerics::float2;
struct Backdrop {
    HWND frame{},content{},original_owner{};int last_width{},last_height{},last_x{},last_y{};float last_radius{-1};bool last_topmost{},last_visible{};
    DesktopWindowTarget target{nullptr};SpriteVisual visual{nullptr};CompositionRoundedRectangleGeometry shape{nullptr};
};
struct CompositionState {
    Windows::System::DispatcherQueueController queue{nullptr};Compositor compositor{nullptr};
    std::unordered_map<HWND,std::unique_ptr<Backdrop>> windows;
};
static CompositionState* state=nullptr;
static LRESULT CALLBACK FrameProc(HWND hwnd,UINT message,WPARAM wp,LPARAM lp){if(message==WM_NCHITTEST)return HTTRANSPARENT;if(message==WM_ERASEBKGND)return 1;return DefWindowProcW(hwnd,message,wp,lp);}
static void Initialize(){if(state)return;auto next=std::make_unique<CompositionState>();if(!Windows::System::DispatcherQueue::GetForCurrentThread()){DispatcherQueueOptions options{sizeof(options),DQTYPE_THREAD_CURRENT,DQTAT_COM_STA};check_hresult(CreateDispatcherQueueController(options,reinterpret_cast<ABI::Windows::System::IDispatcherQueueController**>(put_abi(next->queue))));}next->compositor=Compositor();WNDCLASSW cls{};cls.lpfnWndProc=FrameProc;cls.hInstance=GetModuleHandleW(nullptr);cls.lpszClassName=L"PanneauxClippedBackdrop";RegisterClassW(&cls);state=next.release();}
extern "C" __declspec(dllexport) HRESULT __stdcall BackdropAttach(HWND content){try{
    Initialize();if(state->windows.contains(content))return S_OK;
    auto item=std::make_unique<Backdrop>();item->content=content;item->original_owner=GetWindow(content,GW_OWNER);
    item->frame=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_NOREDIRECTIONBITMAP,L"PanneauxClippedBackdrop",L"",WS_POPUP,0,0,1,1,item->original_owner,nullptr,GetModuleHandleW(nullptr),nullptr);if(!item->frame)return HRESULT_FROM_WIN32(GetLastError());
    BOOL enabled=TRUE;DwmSetWindowAttribute(item->frame,static_cast<DWMWINDOWATTRIBUTE>(17),&enabled,sizeof(enabled));int corner=1;DwmSetWindowAttribute(item->frame,static_cast<DWMWINDOWATTRIBUTE>(33),&corner,sizeof(corner));DWORD border=0xfffffffe;DwmSetWindowAttribute(item->frame,static_cast<DWMWINDOWATTRIBUTE>(34),&border,sizeof(border));
    auto interop=state->compositor.as<ABI::Windows::UI::Composition::Desktop::ICompositorDesktopInterop>();check_hresult(interop->CreateDesktopWindowTarget(item->frame,FALSE,reinterpret_cast<ABI::Windows::UI::Composition::Desktop::IDesktopWindowTarget**>(put_abi(item->target))));
    item->visual=state->compositor.CreateSpriteVisual();item->visual.Brush(state->compositor.CreateHostBackdropBrush());item->shape=state->compositor.CreateRoundedRectangleGeometry();item->visual.Clip(state->compositor.CreateGeometricClip(item->shape));item->target.Root(item->visual);
    SetWindowLongPtrW(content,GWLP_HWNDPARENT,reinterpret_cast<LONG_PTR>(item->frame));state->windows.emplace(content,std::move(item));return S_OK;
}catch(...){return to_hresult();}}
extern "C" __declspec(dllexport) HRESULT __stdcall BackdropUpdate(HWND content,double radius,BOOL visible,BOOL topmost){try{
    if(!state||!state->windows.contains(content))return S_FALSE;auto& item=*state->windows.at(content);if(!visible){if(item.last_visible)ShowWindow(item.frame,SW_HIDE);item.last_visible=false;return S_OK;}
    RECT r{};GetWindowRect(content,&r);int width=r.right-r.left,height=r.bottom-r.top;if(width<1||height<1)return S_FALSE;float round=static_cast<float>(radius*GetDpiForWindow(content)/96.0);bool resized=item.last_width!=width||item.last_height!=height||item.last_radius!=round;if(resized){item.visual.Size(float2{static_cast<float>(width),static_cast<float>(height)});item.shape.Size(float2{static_cast<float>(width),static_cast<float>(height)});item.shape.CornerRadius(float2{round,round});HRGN region=CreateRoundRectRgn(0,0,width+1,height+1,static_cast<int>(round*2),static_cast<int>(round*2));if(!SetWindowRgn(item.frame,region,FALSE))DeleteObject(region);item.last_width=width;item.last_height=height;item.last_radius=round;}
    if(!IsWindowVisible(item.frame)||item.last_topmost!=static_cast<bool>(topmost)){SetWindowPos(item.frame,topmost?HWND_TOPMOST:HWND_NOTOPMOST,r.left,r.top,width,height,SWP_NOACTIVATE);SetWindowPos(item.frame,content,r.left,r.top,width,height,SWP_NOACTIVATE|SWP_SHOWWINDOW);item.last_topmost=topmost!=FALSE;}
    else if(resized||!item.last_visible||item.last_x!=r.left||item.last_y!=r.top)SetWindowPos(item.frame,nullptr,r.left,r.top,width,height,SWP_NOACTIVATE|SWP_NOZORDER|SWP_SHOWWINDOW);item.last_x=r.left;item.last_y=r.top;item.last_visible=true;return S_OK;
}catch(...){return to_hresult();}}
extern "C" __declspec(dllexport) void __stdcall BackdropDetach(HWND content){if(!state)return;auto found=state->windows.find(content);if(found==state->windows.end())return;auto item=std::move(found->second);state->windows.erase(found);if(IsWindow(content))SetWindowLongPtrW(content,GWLP_HWNDPARENT,reinterpret_cast<LONG_PTR>(item->original_owner));item->target.Root(nullptr);item->target.Close();DestroyWindow(item->frame);}
