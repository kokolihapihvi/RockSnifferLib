using RockSnifferLib.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RockSnifferLib.SysHelpers
{
    public class CustomAPI
    {
        const int CNST_SYSTEM_HANDLE_INFORMATION = 16;
        const uint STATUS_INFO_LENGTH_MISMATCH = 0xc0000004;

        private static bool? b64;

        public static List<Win32API.SYSTEM_HANDLE_INFORMATION> GetHandles(Process process)
        {
            uint nStatus;
            int nHandleInfoSize = 0x10000;
            IntPtr ipHandlePointer = Marshal.AllocHGlobal(nHandleInfoSize);
            int nLength = 0;
            IntPtr ipHandle = IntPtr.Zero;

            if (Logger.logSystemHandleQuery)
            {
                Logger.Log("Querying handles");
            }

            //Query handles
            while ((nStatus = Win32API.NtQuerySystemInformation(CNST_SYSTEM_HANDLE_INFORMATION, ipHandlePointer, nHandleInfoSize, ref nLength)) == STATUS_INFO_LENGTH_MISMATCH)
            {
                nHandleInfoSize = nLength;

                Marshal.FreeHGlobal(ipHandlePointer);

                ipHandlePointer = Marshal.AllocHGlobal(nLength);
            }

            //byte[] baTemp = new byte[nLength];
            //Win32API.CopyMemory(baTemp, ipHandlePointer, (uint)nLength);

            //Read the first 4/8 bytes, which is the number of handles
            long lHandleCount = 0;
            if (Is64Bits())
            {
                lHandleCount = Marshal.ReadInt64(ipHandlePointer);
                ipHandle = new IntPtr(ipHandlePointer.ToInt64() + 8);
            }
            else
            {
                lHandleCount = Marshal.ReadInt32(ipHandlePointer);
                ipHandle = new IntPtr(ipHandlePointer.ToInt32() + 4);
            }

            if (Logger.logSystemHandleQuery)
            {
                Logger.Log("Total handle count: {0}", lHandleCount);
            }

            Win32API.SYSTEM_HANDLE_INFORMATION shHandle;
            List<Win32API.SYSTEM_HANDLE_INFORMATION> lstHandles = new List<Win32API.SYSTEM_HANDLE_INFORMATION>();

            //Go through every handle
            for (long lIndex = 0; lIndex < lHandleCount; lIndex++)
            {
                shHandle = new Win32API.SYSTEM_HANDLE_INFORMATION();

                //Read struct from pointer and assign SYSTEM_HANDLE_INFORMATION object
                if (Is64Bits())
                {
                    shHandle = (Win32API.SYSTEM_HANDLE_INFORMATION)Marshal.PtrToStructure(ipHandle, shHandle.GetType());
                    ipHandle = new IntPtr(ipHandle.ToInt64() + Marshal.SizeOf(shHandle));
                }
                else
                {
                    shHandle = (Win32API.SYSTEM_HANDLE_INFORMATION)Marshal.PtrToStructure(ipHandle, shHandle.GetType());
                    ipHandle = new IntPtr(ipHandle.ToInt32() + Marshal.SizeOf(shHandle) - 4);
                }

                //Skip if it belongs to another process
                if (shHandle.ProcessID != process.Id)
                    continue;

                //Add handle to the list
                lstHandles.Add(shHandle);
            }

            if (Logger.logSystemHandleQuery)
            {
                Logger.Log("Handles filtered to {0}[{1}]: {2}", process.ProcessName, process.Id, lstHandles.Count);
            }

            if (lstHandles.Count == 0)
            {
                Logger.LogError("Warning: No handles found for {0}", process.ProcessName);
            }

            //Free memory
            Marshal.FreeHGlobal(ipHandlePointer);

            //Return a list
            return lstHandles;
        }

        public static bool Is64Bits()
        {
            if (b64 == null)
            {
                b64 = Marshal.SizeOf(typeof(IntPtr)) == 8;
            }

            return b64 ?? true;
        }

        public static bool IsRunningOnMono ()
        {
            return Type.GetType ("Mono.Runtime") != null;
        }
    }
}
