# Wpa.Demystifier
Kind of like Ben.Demystifier, but as an add-in for Windows Performance Analyzer!

Transform noisy messes like `MS.Dbg.DbgEngDebugger+DbgEngThread+<>c__DisplayClass13_0'1[System.__Canon]::<ExecuteAsync>b__0 0x0` into things that are faster to read and understand, like `MS.Dbg.DbgEngDebugger+DbgEngThread::ExecuteAsync<T>+()=>{}`!

* Tested with the Windows 1809 ADK version of WPA, but likely compatible with previous Windows 10 ADK versions as well.
* Only changes the 'Stack' column; the 'Stack (Frame Tags)' still shows the same ugly mess.
  * Doing the frame tags column in a reasonable way requires figuring out a detour technique that works struct member functions
* Method signatures longer than ~258 characters get truncated before the add-in gets a chance to simplify & shorten them (somewhere in perf_nt.dll, native C++ code), so unfortunately will not be improved by this
* Also trims the trailing " 0x0" off of Javascript stack traces (As well as WARP and VPC, assuming they have the same and not actual offsets)


To install, just drop Wpa.Demystifier.dll in the Windows Performance Toolkit directory and add a line for it in wpa.addins.ini