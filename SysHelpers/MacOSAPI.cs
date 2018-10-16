using System;
using System.Runtime.InteropServices;

namespace RockSnifferLib.SysHelpers
{
    public static class MacOSAPI
    {
        public struct Region
        {
            public ulong Address;
            public ulong Size;
            public int Protection;
        }
        [DllImport("libMacOSAPI.dylib")]
        public static extern int vm_read_wrapper(uint TargetTask, ulong Address, ulong Size, out IntPtr Data, out int DataCount);
        [DllImport("libMacOSAPI.dylib")]
        public static extern int find_main_binary_wrapper(ulong ProcessPid, out ulong Offset);
        [DllImport("libMacOSAPI.dylib")]
        public static extern int task_for_pid_wrapper(ulong ProcessPid, out uint Task);
        [DllImport("libMacOSAPI.dylib")]
        public static extern int mach_vm_region_wrapper(ulong TargetTask, out ulong Address, out ulong Size, out int Prot);
        [DllImport("libMacOSAPI.dylib")]
        public static extern int mach_vm_region_recurse_wrapper(ulong TargetTask, out ulong Address, out ulong Size, out UInt32 userTag);

    }
}
