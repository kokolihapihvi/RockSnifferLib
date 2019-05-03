using System;
using System.Runtime.InteropServices;

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

        public static string ReadStringFromMemory(IntPtr processHandle, IntPtr address, int maxLength = 128)
        {
            //Dont read garbage
            if (address == IntPtr.Zero)
            {
                return null;
            }

            byte[] bytes = ReadBytesFromMemory(processHandle, address, maxLength);

            //Find the first 0 in the array
            int end = Array.IndexOf<byte>(bytes, 0);

            //No terminating 0 in the array, or 0 length string
            if (end <= 0)
            {
                return null;
            }

            //Copy into a char array
            char[] chars = new char[end];

            Array.Copy(bytes, chars, end);

            //Verify that all characters are in range 32-126 (basic ascii)
            foreach (char c in chars)
            {
                if(c < 32 || c > 126)
                {
                    return null;
                }
            }

            //Create string from char array
            string str = new string(chars);

            return str;
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

        /// <summary>
        /// Read a structure from a processes memory
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="rsProcessHandle"></param>
        /// <param name="structAddress"></param>
        /// <returns></returns>
        public static T ReadStructureFromMemory<T>(IntPtr rsProcessHandle, IntPtr structAddress)
        {
            //Determine size of the structure
            int size = Marshal.SizeOf<T>();

            //Read the structure from rs memory
            byte[] buffer = ReadBytesFromMemory(rsProcessHandle, structAddress, size);

            //Pin the object in memory
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

            //Marshal the memory to the struct
            T obj = Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());

            //Free the pinned object
            handle.Free();

            //Return marshaled struct
            return obj;
        }
    }
}
