using System;
using System.Runtime.InteropServices;

/*
    Pointer Scan Info: 
    
    CheatManager 6.2M doesnt have pointer scanner support so Bit-Slicer was used to find the pointer chains for macos. 
    The original repo is at https://github.com/zorgiepoo/Bit-Slicer, (pointer_scanner branch). A fork with updated master
    and working pointer scan is maintained here https://github.com/sandiz/Bit-Slicer/tree/pointer_scanner

    The tool is limited and gives a lot of false positives, so far pointer chains for song_id and song_timer has been found
    for note data RockSniffer will search the memory first for the values.

    TODO: find pointer chain for note data
 */
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
        [DllImport("libMacOSAPI.dylib")]
        public static extern IntPtr proc_pidinfo_wrapper(ulong pid, out int numPath);
        [DllImport("libMacOSAPI.dylib")]
        public static extern void free_wrapper(IntPtr ptr);
        [DllImport("libMacOSAPI.dylib")]
        public static extern int vm_deallocate_wrapper(ulong TargetTask, ulong Address, ulong Size);
        [DllImport("libMacOSAPI.dylib")]
        public static extern ulong scan_mem(ulong TargetTask, ulong Address, ulong Size, ulong DataIndex, int magic);
        [DllImport("libMacOSAPI.dylib")]
        public static extern ulong scan_mem_char(ulong TargetTask, ulong Address, ulong Size,
        ulong DataIndex, byte[] hint1, int hint1Size, byte[] hint2, int hint2Size);


    }
}
