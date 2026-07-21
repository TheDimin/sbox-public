using System;
using System.Runtime.InteropServices;

namespace Sandbox.Mounting;

sealed class FmodApi
{
	internal FmodApi( FmodNativeLibrary libraries )
	{
		SystemCreate = libraries.GetStudioExport<SystemCreateDelegate>( "FMOD_Studio_System_Create" );
		SystemInitialize = libraries.GetStudioExport<SystemInitializeDelegate>( "FMOD_Studio_System_Initialize" );
		SystemRelease = libraries.GetStudioExport<HandleDelegate>( "FMOD_Studio_System_Release" );
		SystemUpdate = libraries.GetStudioExport<HandleDelegate>( "FMOD_Studio_System_Update" );
		SystemLoadBankFile = libraries.GetStudioExport<SystemLoadBankFileDelegate>( "FMOD_Studio_System_LoadBankFile" );
		SystemUnloadAll = libraries.GetStudioExport<HandleDelegate>( "FMOD_Studio_System_UnloadAll" );
		SystemGetEvent = libraries.GetStudioExport<SystemGetEventDelegate>( "FMOD_Studio_System_GetEvent" );
		BankUnload = libraries.GetStudioExport<HandleDelegate>( "FMOD_Studio_Bank_Unload" );
		EventDescriptionCreateInstance = libraries.GetStudioExport<CreateInstanceDelegate>( "FMOD_Studio_EventDescription_CreateInstance" );
		EventInstanceStart = libraries.GetStudioExport<HandleDelegate>( "FMOD_Studio_EventInstance_Start" );
		EventInstanceRelease = libraries.GetStudioExport<HandleDelegate>( "FMOD_Studio_EventInstance_Release" );
	}

	internal SystemCreateDelegate SystemCreate { get; }
	internal SystemInitializeDelegate SystemInitialize { get; }
	internal HandleDelegate SystemRelease { get; }
	internal HandleDelegate SystemUpdate { get; }
	internal SystemLoadBankFileDelegate SystemLoadBankFile { get; }
	internal HandleDelegate SystemUnloadAll { get; }
	internal SystemGetEventDelegate SystemGetEvent { get; }
	internal HandleDelegate BankUnload { get; }
	internal CreateInstanceDelegate EventDescriptionCreateInstance { get; }
	internal HandleDelegate EventInstanceStart { get; }
	internal HandleDelegate EventInstanceRelease { get; }

	[UnmanagedFunctionPointer( CallingConvention.Winapi )]
	internal delegate int SystemCreateDelegate( out IntPtr system, uint headerVersion );

	[UnmanagedFunctionPointer( CallingConvention.Winapi )]
	internal delegate int SystemInitializeDelegate( IntPtr system, int maxChannels, uint studioFlags, uint coreFlags, IntPtr extraDriverData );

	[UnmanagedFunctionPointer( CallingConvention.Winapi )]
	internal delegate int HandleDelegate( IntPtr handle );

	[UnmanagedFunctionPointer( CallingConvention.Winapi )]
	internal delegate int SystemLoadBankFileDelegate( IntPtr system, IntPtr filename, uint flags, out IntPtr bank );

	[UnmanagedFunctionPointer( CallingConvention.Winapi )]
	internal delegate int SystemGetEventDelegate( IntPtr system, IntPtr path, out IntPtr eventDescription );

	[UnmanagedFunctionPointer( CallingConvention.Winapi )]
	internal delegate int CreateInstanceDelegate( IntPtr eventDescription, out IntPtr eventInstance );
}
