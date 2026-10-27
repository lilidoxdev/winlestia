# Python3.12 / screen_picker.py / Temporary click capture on a dedicated Windows message thread.
import ctypes,threading
from ctypes import wintypes
class ClickPicker:
 def __init__(self):self.thread_id=0;self.handle=None;self.choice=None;self.cancel=False;self.failed=False;self.stopped=threading.Event();self.thread=threading.Thread(target=self.run,daemon=True);self.thread.start()
 def run(self):
  user=ctypes.WinDLL('user32',use_last_error=True);kernel=ctypes.WinDLL('kernel32',use_last_error=True)
  callback_type=ctypes.WINFUNCTYPE(ctypes.c_ssize_t,ctypes.c_int,ctypes.c_size_t,ctypes.c_void_p)
  user.SetWindowsHookExW.argtypes=[ctypes.c_int,callback_type,ctypes.c_void_p,ctypes.c_ulong];user.SetWindowsHookExW.restype=ctypes.c_void_p
  user.CallNextHookEx.argtypes=[ctypes.c_void_p,ctypes.c_int,ctypes.c_size_t,ctypes.c_void_p];user.CallNextHookEx.restype=ctypes.c_ssize_t
  user.UnhookWindowsHookEx.argtypes=[ctypes.c_void_p];kernel.GetModuleHandleW.restype=ctypes.c_void_p
  down=None
  def mouse(code,message,data):
   nonlocal down
   if code>=0:
    if message in (0x201,0x204):down=(ctypes.cast(data,ctypes.POINTER(wintypes.POINT)).contents.x,ctypes.cast(data,ctypes.POINTER(wintypes.POINT)).contents.y);return 1
    if message in (0x202,0x205) and down is not None:
     if message==0x205:self.cancel=True
     else:self.choice=down
     return 1
   return user.CallNextHookEx(self.handle,code,message,data)
  callback=callback_type(mouse);msg=wintypes.MSG();self.thread_id=kernel.GetCurrentThreadId();user.PeekMessageW(ctypes.byref(msg),None,0,0,0)
  try:
   self.handle=user.SetWindowsHookExW(14,callback,kernel.GetModuleHandleW(None),0)
   if not self.handle:self.failed=True;return
   while not self.stopped.is_set() and user.GetMessageW(ctypes.byref(msg),None,0,0)>0:pass
  except Exception:self.failed=True
  finally:
   if self.handle:user.UnhookWindowsHookEx(self.handle)
   self.handle=None
 def stop(self):
  self.stopped.set()
  if self.thread_id:ctypes.windll.user32.PostThreadMessageW(self.thread_id,0x12,0,0)
  self.thread.join(.2)
