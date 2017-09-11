using System;

namespace RockSnifferLib.SysHelpers
{
    class MemoryHelper
    {
        /// <summary>
        /// Read a number of bytes from a processes memory into given byte array buffer
        /// </summary>
        /// <param name="processHandle"></param>
        /// <param name="address"></param>
        /// <param name="bytes"></param>
        /// <returns>bytes read</returns>
        public static int ReadBytesFromMemory(IntPtr processHandle, IntPtr address, int bytes, ref byte[] buffer)
        {
            int bytesRead = 0;

            Win32API.ReadProcessMemory((int)processHandle, (int)address, buffer, bytes, ref bytesRead);

            return bytesRead;
        }

        /// <summary>
        /// Read a number of bytes from a processes memory into a byte array
        /// </summary>
        /// <param name="processHandle"></param>
        /// <param name="address"></param>
        /// <param name="bytes"></param>
        /// <returns>bytes read</returns>
        public static byte[] ReadBytesFromMemory(IntPtr processHandle, IntPtr address, int bytes)
        {
            int bytesRead = 0;
            byte[] buf = new byte[bytes];

            Win32API.ReadProcessMemory((int)processHandle, (int)address, buf, bytes, ref bytesRead);

            return buf;
        }

        /// <summary>
        /// Reads an Int32 from a processes memory
        /// </summary>
        /// <param name="processHandle"></param>
        /// <param name="address"></param>
        /// <returns></returns>
        public static int ReadInt32FromMemory(IntPtr processHandle, IntPtr address)
        {
            return BitConverter.ToInt32(ReadBytesFromMemory(processHandle, address, 4), 0);
        }

        /// <summary>
        /// Reads a single byte from a processes memory
        /// </summary>
        /// <param name="processHandle"></param>
        /// <param name="address"></param>
        /// <returns></returns>
        public static byte ReadByteFromMemory(IntPtr processHandle, IntPtr address)
        {
            return ReadBytesFromMemory(processHandle, address, 1)[0];
        }

        /// <summary>
        /// Follows a pointer by reading destination address from process memory and applying offset
        /// <para></para>
        /// Returns the new pointer
        /// </summary>
        /// <param name="processHandle"></param>
        /// <param name="address"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static IntPtr FollowPointer(IntPtr processHandle, IntPtr address, int offset)
        {
            IntPtr readPointer = (IntPtr)ReadInt32FromMemory(processHandle, address);

            return IntPtr.Add(readPointer, offset);
        }

        /// <summary>
        /// Reads a float from a processes memory
        /// </summary>
        /// <param name="processHandle"></param>
        /// <param name="address"></param>
        /// <returns></returns>
        public static float ReadFloatFromMemory(IntPtr processHandle, IntPtr address)
        {
            return BitConverter.ToSingle(ReadBytesFromMemory(processHandle, address, 4), 0);
        }
    }
}
