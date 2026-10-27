// C++20 / ProcessAudio.cpp / Windows 11. Process-tree loopback; in-memory PCM only.
#include <windows.h>
#include <audioclient.h>
#include <audioclientactivationparams.h>
#include <mmdeviceapi.h>
#include <wrl.h>
#include <atomic>
#include <thread>
#include <mutex>
#include <vector>
using Microsoft::WRL::ComPtr;
class Activation final:public IActivateAudioInterfaceCompletionHandler,public IAgileObject{
 std::atomic<ULONG> refs{1};
public: HANDLE done=CreateEventW(nullptr,FALSE,FALSE,nullptr);HRESULT result=E_PENDING;ComPtr<IAudioClient> client;
 ~Activation(){CloseHandle(done);} ULONG STDMETHODCALLTYPE AddRef()override{return ++refs;}ULONG STDMETHODCALLTYPE Release()override{auto n=--refs;if(!n)delete this;return n;}
 HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** p)override{if(!p)return E_POINTER;*p=nullptr;if(id==__uuidof(IUnknown)||id==__uuidof(IActivateAudioInterfaceCompletionHandler))*p=static_cast<IActivateAudioInterfaceCompletionHandler*>(this);else if(id==__uuidof(IAgileObject))*p=static_cast<IAgileObject*>(this);else return E_NOINTERFACE;AddRef();return S_OK;}
 HRESULT STDMETHODCALLTYPE ActivateCompleted(IActivateAudioInterfaceAsyncOperation* operation)override{ComPtr<IUnknown> value;HRESULT hr;result=operation->GetActivateResult(&hr,&value);if(SUCCEEDED(result))result=hr;if(SUCCEEDED(result))result=value.As(&client);SetEvent(done);return S_OK;}
};
struct Capture{std::atomic<bool> stop{false};std::atomic<long> status{E_PENDING};std::thread worker;std::mutex gate;std::vector<float> latest;unsigned serial=0;Capture(DWORD pid){worker=std::thread([this,pid]{run(pid);});}~Capture(){stop=true;if(worker.joinable())worker.join();}
 void run(DWORD pid){CoInitializeEx(nullptr,COINIT_MULTITHREADED);{
 ComPtr<Activation> activation;activation.Attach(new Activation);AUDIOCLIENT_ACTIVATION_PARAMS params{};params.ActivationType=AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;params.ProcessLoopbackParams.TargetProcessId=pid;params.ProcessLoopbackParams.ProcessLoopbackMode=PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;
 PROPVARIANT prop{};prop.vt=VT_BLOB;prop.blob.cbSize=sizeof(params);prop.blob.pBlobData=reinterpret_cast<BYTE*>(&params);ComPtr<IActivateAudioInterfaceAsyncOperation> operation;
 HRESULT hr=ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,__uuidof(IAudioClient),&prop,activation.Get(),&operation);
 if(SUCCEEDED(hr)){if(WaitForSingleObject(activation->done,5000)!=WAIT_OBJECT_0)hr=HRESULT_FROM_WIN32(ERROR_TIMEOUT);else hr=activation->result;}
 if(SUCCEEDED(hr)){WAVEFORMATEX format{};format.wFormatTag=WAVE_FORMAT_PCM;format.nChannels=2;format.nSamplesPerSec=44100;format.wBitsPerSample=16;format.nBlockAlign=4;format.nAvgBytesPerSec=176400;
 hr=activation->client->Initialize(AUDCLNT_SHAREMODE_SHARED,AUDCLNT_STREAMFLAGS_LOOPBACK,200000,0,&format,nullptr);ComPtr<IAudioCaptureClient> reader;if(SUCCEEDED(hr))hr=activation->client->GetService(IID_PPV_ARGS(&reader));if(SUCCEEDED(hr))hr=activation->client->Start();status=hr;
 while(SUCCEEDED(hr)&&!stop){UINT32 packet=0;hr=reader->GetNextPacketSize(&packet);while(SUCCEEDED(hr)&&packet){BYTE* data=nullptr;UINT32 frames;DWORD flags;hr=reader->GetBuffer(&data,&frames,&flags,nullptr,nullptr);if(FAILED(hr))break;{std::lock_guard lock(gate);auto pcm=reinterpret_cast<short*>(data);for(UINT32 i=0;i<frames;i++)latest.push_back((flags&AUDCLNT_BUFFERFLAGS_SILENT)?0.f:(pcm[i*2]+pcm[i*2+1])/65536.f);if(latest.size()>8192)latest.erase(latest.begin(),latest.end()-8192);serial++;}reader->ReleaseBuffer(frames);hr=reader->GetNextPacketSize(&packet);}Sleep(10);}activation->client->Stop();}
 status=hr;
 }CoUninitialize();}
};
extern "C" __declspec(dllexport) Capture* __cdecl AudioCreate(unsigned pid){try{return pid?new Capture(pid):nullptr;}catch(...){return nullptr;}}
extern "C" __declspec(dllexport) void __cdecl AudioDestroy(Capture* c){delete c;}
extern "C" __declspec(dllexport) int __cdecl AudioRead(Capture* c,float* output,int count,unsigned* serial){if(!c||!output||count<=0)return 0;std::lock_guard lock(c->gate);if(c->latest.size()<static_cast<size_t>(count))return 0;std::copy(c->latest.end()-count,c->latest.end(),output);*serial=c->serial;return count;}
extern "C" __declspec(dllexport) long __cdecl AudioStatus(Capture* c){return c?c->status.load():E_POINTER;}
