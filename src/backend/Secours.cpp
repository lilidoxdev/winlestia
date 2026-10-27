// language: C++20, file: Secours.cpp, target: Windows 11 x64, MSVC /MT
#define UNICODE
#define _UNICODE
#define NOMINMAX
#include <windows.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <exdisp.h>
#include <shlguid.h>
#include <wrl/client.h>
#include <tlhelp32.h>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>
#include <map>
#include <algorithm>
#include <stdexcept>
using Microsoft::WRL::ComPtr;
namespace fs=std::filesystem;
struct DesktopIcon {std::wstring name;POINT point{};PIDLIST_RELATIVE pidl=nullptr;};
static void check(HRESULT result,const char* message){if(FAILED(result))throw std::runtime_error(message);}
static fs::path app_root(){PWSTR p=nullptr;check(SHGetKnownFolderPath(FOLDERID_Profile,0,nullptr,&p),"Profil Windows inaccessible.");fs::path root=fs::path(p)/L"Tools"/L"Panneaux"/L"Glass-v5";CoTaskMemFree(p);std::error_code error;fs::create_directories(root,error);return root;}
static std::string utf8(const std::wstring& text){int n=WideCharToMultiByte(CP_UTF8,0,text.data(),(int)text.size(),nullptr,0,nullptr,nullptr);std::string out(n,0);WideCharToMultiByte(CP_UTF8,0,text.data(),(int)text.size(),out.data(),n,nullptr,nullptr);return out;}
static std::wstring wide(const std::string& text){int n=MultiByteToWideChar(CP_UTF8,0,text.data(),(int)text.size(),nullptr,0);std::wstring out(n,0);MultiByteToWideChar(CP_UTF8,0,text.data(),(int)text.size(),out.data(),n);return out;}
static ComPtr<IFolderView2> desktop_view(){
    ComPtr<IShellWindows> windows;check(CoCreateInstance(CLSID_ShellWindows,nullptr,CLSCTX_ALL,IID_PPV_ARGS(&windows)),"Explorateur Windows inaccessible.");
    VARIANT location{},empty{};location.vt=VT_I4;location.lVal=CSIDL_DESKTOP;long hwnd=0;ComPtr<IDispatch> dispatch;
    check(windows->FindWindowSW(&location,&empty,SWC_DESKTOP,&hwnd,SWFO_NEEDDISPATCH,&dispatch),"Bureau Windows introuvable.");
    ComPtr<IServiceProvider> provider;check(dispatch.As(&provider),"Service Bureau indisponible.");ComPtr<IShellBrowser> browser;
    check(provider->QueryService(SID_STopLevelBrowser,IID_PPV_ARGS(&browser)),"Navigateur du Bureau inaccessible.");
    ComPtr<IShellView> view;check(browser->QueryActiveShellView(&view),"Vue du Bureau inaccessible.");ComPtr<IFolderView2> folder;check(view.As(&folder),"Interface du Bureau indisponible.");return folder;
}
static std::vector<DesktopIcon> read_icons(IFolderView2* view){
    ComPtr<IShellFolder> folder;check(view->GetFolder(IID_PPV_ARGS(&folder)),"Dossier du Bureau inaccessible.");int count=0;check(view->ItemCount(SVGIO_ALLVIEW,&count),"Lecture des icones impossible.");std::vector<DesktopIcon> result;
    for(int i=0;i<count;i++){DesktopIcon icon;if(FAILED(view->Item(i,&icon.pidl)))continue;STRRET name{};WCHAR buffer[1024]{};if(SUCCEEDED(folder->GetDisplayNameOf(icon.pidl,SHGDN_NORMAL,&name))&&SUCCEEDED(StrRetToBufW(&name,icon.pidl,buffer,1024))&&SUCCEEDED(view->GetItemPosition(icon.pidl,&icon.point))){icon.name=buffer;result.push_back(icon);}else CoTaskMemFree(icon.pidl);}
    return result;
}
static void snapshot(const std::vector<DesktopIcon>& icons){auto path=app_root()/L"bureau-avant-glass.tsv";if(fs::exists(path))return;std::ofstream output(path,std::ios::binary);if(!output)throw std::runtime_error("Sauvegarde des positions impossible.");for(const auto& icon:icons)output<<icon.point.x<<'\t'<<icon.point.y<<'\t'<<utf8(icon.name)<<'\n';}
static bool contains(std::wstring name,const std::vector<std::wstring>& words){std::transform(name.begin(),name.end(),name.begin(),towlower);for(const auto& word:words)if(name.find(word)!=std::wstring::npos)return true;return false;}
static int group(const std::wstring& name){
    if(contains(name,{L"apex",L"assetto",L"aion",L"ambitions",L"deadlock",L"marvel",L"overwatch",L"riders",L"rocket",L"rust",L"hogwarts",L"crimson",L"neverness",L"osu!",L"valorant",L"league of",L"teamfight",L"snowrunner",L"coaster",L"fragpunk",L"aniimo",L"steam",L"battle.net",L"epic games",L"ubisoft",L"riot client",L"client riot",L"content manager",L"satisfactory",L"wand",L"wemod",L"hydra"})||name==L"EA")return 2;
    if(contains(name,{L"03_projets",L"04_medias",L"05_administratif",L"comparaison",L"extenstion",L"lisez",L"corbeille",L"secours"}))return 1;
    return 0;
}
static void stop_panels(){
    for(auto event_name:{L"Local\\PanneauxWorkspaceV4Quit",L"Local\\PanneauxGlassV5Quit"}){HANDLE signal=OpenEventW(EVENT_MODIFY_STATE,FALSE,event_name);if(signal){SetEvent(signal);CloseHandle(signal);}}
    Sleep(250);
    HANDLE snap=CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS,0);if(snap==INVALID_HANDLE_VALUE)return;PROCESSENTRY32W entry{sizeof(entry)};
    if(Process32FirstW(snap,&entry))do{if(_wcsicmp(entry.szExeFile,L"Panneaux.Workspace.exe")&&_wcsicmp(entry.szExeFile,L"Panneaux.Glass.exe"))continue;HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|PROCESS_TERMINATE|SYNCHRONIZE,FALSE,entry.th32ProcessID);if(!process)continue;WCHAR path[32768];DWORD length=32768;if(QueryFullProcessImageNameW(process,0,path,&length)){auto root=app_root().parent_path().wstring();if(std::wstring(path).rfind(root+L"\\",0)==0&&WaitForSingleObject(process,1000)==WAIT_TIMEOUT)TerminateProcess(process,0);}CloseHandle(process);}while(Process32NextW(snap,&entry));CloseHandle(snap);
    HKEY run=nullptr;if(RegOpenKeyExW(HKEY_CURRENT_USER,L"Software\\Microsoft\\Windows\\CurrentVersion\\Run",0,KEY_QUERY_VALUE|KEY_SET_VALUE,&run)==ERROR_SUCCESS){WCHAR value[32768];DWORD bytes=sizeof(value);if(RegQueryValueExW(run,L"Panneaux",nullptr,nullptr,(LPBYTE)value,&bytes)==ERROR_SUCCESS){std::wstring previous=value;if(previous.find(L"\\Panneaux\\")!=std::wstring::npos){std::ofstream backup(app_root()/L"demarrage-avant-secours.txt",std::ios::binary|std::ios::trunc);backup<<utf8(previous);RegDeleteValueW(run,L"Panneaux");}}RegCloseKey(run);}
}
static int restore(bool exact,bool quiet,bool arrange_only){
    auto view=desktop_view();auto icons=read_icons(view.Get());if(icons.empty())throw std::runtime_error("Aucune icone du Bureau lisible.");snapshot(icons);
    if(!arrange_only)stop_panels();
    check(view->SetCurrentFolderFlags(FWF_NOICONS|FWF_AUTOARRANGE,0),"Impossible de rendre les icones visibles.");
    HKEY advanced=nullptr;if(RegCreateKeyExW(HKEY_CURRENT_USER,L"Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced",0,nullptr,0,KEY_SET_VALUE,nullptr,&advanced,nullptr)==ERROR_SUCCESS){DWORD hidden=0;RegSetValueExW(advanced,L"HideIcons",0,REG_DWORD,(BYTE*)&hidden,sizeof(hidden));RegCloseKey(advanced);}
    std::map<std::wstring,std::vector<POINT>> saved;std::map<std::wstring,size_t> consumed;if(exact){std::ifstream file(app_root()/L"bureau-avant-glass.tsv",std::ios::binary);std::string line;while(std::getline(file,line)){std::istringstream input(line);POINT p{};std::string name;if(input>>p.x>>p.y){input.get();std::getline(input,name);saved[wide(name)].push_back(p);}}}
    std::stable_sort(icons.begin(),icons.end(),[](auto& a,auto& b){return a.name<b.name;});POINT spacing{};view->GetSpacing(&spacing);int dx=std::clamp((int)spacing.x,90,160),dy=std::clamp((int)spacing.y,95,160);RECT work{};SystemParametersInfoW(SPI_GETWORKAREA,0,&work,0);int rows=std::max(1,(int)(work.bottom-work.top-48)/dy);int width=work.right-work.left;
    int software_count=(int)std::count_if(icons.begin(),icons.end(),[](const auto& i){return group(i.name)==0;});int origin_x=24,origin_y=24; for(const auto& icon:icons){if(icon.point.x>=0)origin_x=std::min(origin_x,(int)icon.point.x);if(icon.point.y>=0)origin_y=std::min(origin_y,(int)icon.point.y);} int right_col=std::max(0,(width-origin_x-dx)/dx); int folder_col=(software_count+rows-1)/rows+1;int counters[3]{};std::vector<PCUITEMID_CHILD> pidls;std::vector<POINT> points;
    for(const auto& icon:icons){POINT target{};auto found=saved.find(icon.name);size_t occurrence=consumed[icon.name]++;if(exact&&found!=saved.end()&&occurrence<found->second.size())target=found->second[occurrence];else{int g=group(icon.name),index=counters[g]++;int col=index/rows,row=index%rows;target.y=origin_y+row*dy;target.x=origin_x+(g==2?right_col-col:col+(g==1?folder_col:0))*dx;}pidls.push_back(icon.pidl);points.push_back(target);}
    if(exact){
        // Explorer avoids occupied cells. Park in genuinely free cells before restoring the saved layout.
        std::vector<POINT> parking;
        for(int col=0;col<=right_col&&parking.size()<icons.size();++col)for(int row=0;row<rows&&parking.size()<icons.size();++row){POINT p{origin_x+col*dx,origin_y+row*dy};bool reserved=false;for(const auto& target:points)if(abs(target.x-p.x)<dx/2&&abs(target.y-p.y)<dy/2){reserved=true;break;}if(!reserved)for(const auto& icon:icons)if(abs(icon.point.x-p.x)<dx/2&&abs(icon.point.y-p.y)<dy/2){reserved=true;break;}if(!reserved)parking.push_back(p);}
        if(parking.size()==icons.size()){check(view->SelectAndPositionItems((UINT)pidls.size(),pidls.data(),parking.data(),SVSI_POSITIONITEM),"Placement temporaire du Bureau impossible.");Sleep(250);}
    }
    check(view->SelectAndPositionItems((UINT)pidls.size(),pidls.data(),points.data(),SVSI_POSITIONITEM),"Le Bureau refuse le placement des icones.");
    Sleep(400); std::ofstream placement(app_root()/L"secours-positions.tsv",std::ios::binary|std::ios::trunc); int matches=0;for(size_t i=0;i<icons.size();++i){POINT actual{};if(SUCCEEDED(view->GetItemPosition(icons[i].pidl,&actual))&&abs(actual.x-points[i].x)<=6&&abs(actual.y-points[i].y)<=6)matches++;placement<<utf8(icons[i].name)<<"\t"<<points[i].x<<","<<points[i].y<<"\t"<<actual.x<<","<<actual.y<<"\n";CoTaskMemFree(icons[i].pidl);}
    std::ofstream report(app_root()/L"secours-resultat.txt",std::ios::binary|std::ios::trunc);report<<"Visible: true\nIcons: "<<icons.size()<<"\nPositioned: "<<matches<<"\nMode: "<<(exact?"exact":"applications-left-games-right")<<"\n";
    if(matches!=(int)icons.size())throw std::runtime_error("Le Bureau est visible, mais certaines positions n'ont pas ete appliquees. Le detail est dans secours-resultat.txt.");
    if(!quiet)MessageBoxW(nullptr,L"Le Bureau Windows est de retour.\n\nLogiciels a gauche, dossiers au centre, jeux a droite.\nPanneaux est arrete et son demarrage automatique desactive.\n\nAucun fichier n'a ete deplace ou supprime.",L"Bureau restaure",MB_OK|MB_ICONINFORMATION);
    return 0;
}
int WINAPI wWinMain(HINSTANCE,HINSTANCE,PWSTR arguments,int){
    SetProcessDPIAware();HRESULT init=CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);if(FAILED(init))return 2;int result=0;std::wstring args=arguments;
    try{auto view=desktop_view(); if(args.find(L"--hide")!=std::wstring::npos||args.find(L"--show-only")!=std::wstring::npos){bool hidden=args.find(L"--hide")!=std::wstring::npos;HKEY key=nullptr;if(RegCreateKeyExW(HKEY_CURRENT_USER,L"Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced",0,nullptr,0,KEY_SET_VALUE,nullptr,&key,nullptr)==ERROR_SUCCESS){DWORD value=hidden?1:0;RegSetValueExW(key,L"HideIcons",0,REG_DWORD,(BYTE*)&value,sizeof(value));RegCloseKey(key);}DWORD want=hidden?FWF_NOICONS:0;for(int attempt=0;attempt<3;++attempt){check(view->SetCurrentFolderFlags(FWF_NOICONS,want),"Visibilite du Bureau non appliquee.");DWORD live=0;if(SUCCEEDED(view->GetCurrentFolderFlags(&live))&&((live&FWF_NOICONS)==want))break;SendMessageTimeoutW(HWND_BROADCAST,WM_SETTINGCHANGE,0,(LPARAM)L"ShellState",SMTO_ABORTIFHUNG,200,nullptr);Sleep(150);}SendMessageTimeoutW(HWND_BROADCAST,WM_SETTINGCHANGE,0,(LPARAM)L"ShellState",SMTO_ABORTIFHUNG,200,nullptr);view.Reset();CoUninitialize();return 0;} auto icons=read_icons(view.Get());snapshot(icons);for(auto& icon:icons)CoTaskMemFree(icon.pidl);if(args.find(L"--snapshot")==std::wstring::npos)result=restore(args.find(L"--exact")!=std::wstring::npos,args.find(L"--silent")!=std::wstring::npos,args.find(L"--arrange-only")!=std::wstring::npos);}catch(const std::exception& error){std::ofstream log(app_root()/L"secours-erreur.txt",std::ios::binary|std::ios::trunc);log<<error.what();if(args.find(L"--silent")==std::wstring::npos)MessageBoxW(nullptr,wide(error.what()).c_str(),L"Secours du Bureau",MB_OK|MB_ICONERROR);result=1;}
    CoUninitialize();return result;
}
