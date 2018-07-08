using RockSnifferLib.Logging;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace RockSnifferLib.SysHelpers
{
    class FileDetails
    {
        public string Name {
            get; set;
        }

        public static FileDetails GetFileDetails(IntPtr processHandle, Win32API.SYSTEM_HANDLE_INFORMATION sYSTEM_HANDLE_INFORMATION)
        {
            FileDetails fd = new FileDetails();
            fd.Name = "";

            Win32API.OBJECT_BASIC_INFORMATION objBasic = new Win32API.OBJECT_BASIC_INFORMATION();
            Win32API.OBJECT_TYPE_INFORMATION objObjectType = new Win32API.OBJECT_TYPE_INFORMATION();
            Win32API.OBJECT_NAME_INFORMATION objObjectName = new Win32API.OBJECT_NAME_INFORMATION();

            IntPtr ipHandle = IntPtr.Zero;
            IntPtr ipBasic = IntPtr.Zero;
            IntPtr ipObjectType = IntPtr.Zero;
            IntPtr ipObjectName = IntPtr.Zero;
            IntPtr ipTemp = IntPtr.Zero;

            string strObjectTypeName = "";
            string strObjectName = "";
            int nLength = 0;
            int nReturn = 0;

            if (Logger.logFileDetailQuery)
            {
                Logger.Log("Querying file details for handle {0}", sYSTEM_HANDLE_INFORMATION.Handle);
            }

            //OpenProcessForHandle(sYSTEM_HANDLE_INFORMATION.ProcessID);

            //Duplicate handle into our process, return if not successful
            if (!Win32API.DuplicateHandle(processHandle, sYSTEM_HANDLE_INFORMATION.Handle, Win32API.GetCurrentProcess(), out ipHandle, 0, false, Win32API.DUPLICATE_SAME_ACCESS))
                return null;

            if (Logger.logFileDetailQuery)
            {
                Logger.Log("Duplicated handle, querying OBJECT_BASIC_INFORMATION");
            }

            //Check GrantedAccess against a constant from the internet because they said so :/
            if (sYSTEM_HANDLE_INFORMATION.GrantedAccess == 0x0012019f)
            {
                if (Logger.logFileDetailQuery)
                {
                    Logger.Log("GrantedAccess check failed");
                }

                //Close the duplicated handle and return
                Win32API.CloseHandle(ipHandle);
                return null;
            }

            //Query basic information for type and name length
            //Allocate memory
            ipBasic = Marshal.AllocHGlobal(Marshal.SizeOf(objBasic));
            //Query
            Win32API.NtQueryObject(ipHandle, (int)Win32API.ObjectInformationClass.ObjectBasicInformation, ipBasic, Marshal.SizeOf(objBasic), ref nLength);
            //Read struct
            objBasic = (Win32API.OBJECT_BASIC_INFORMATION)Marshal.PtrToStructure(ipBasic, objBasic.GetType());
            //Free memory
            Marshal.FreeHGlobal(ipBasic);

            if (Logger.logFileDetailQuery)
            {
                Logger.Log("Querying OBJECT_TYPE_INFORMATION");
            }

            //Query object type information
            //Allocate memory
            nLength = objBasic.TypeInformationLength;
            ipObjectType = Marshal.AllocHGlobal(nLength);

            //Query, checking for length mismatch
            while ((uint)(nReturn = Win32API.NtQueryObject(ipHandle, (int)Win32API.ObjectInformationClass.ObjectTypeInformation, ipObjectType, nLength, ref nLength)) == Win32API.STATUS_INFO_LENGTH_MISMATCH)
            {
                //Re-allocate memory if length mismatch
                Marshal.FreeHGlobal(ipObjectType);
                ipObjectType = Marshal.AllocHGlobal(nLength);
            }

            //Read struct
            objObjectType = (Win32API.OBJECT_TYPE_INFORMATION)Marshal.PtrToStructure(ipObjectType, objObjectType.GetType());

            /*
            //Get pointer to object type name UNICODE_STRING
            if (CustomAPI.Is64Bits())
            {
                ipTemp = new IntPtr(Convert.ToInt64(objObjectType.Name.Buffer.ToString(), 10) >> 32);
            }
            else
            {
                ipTemp = objObjectType.Name.Buffer;
            }
            */

            if (Logger.logFileDetailQuery)
            {
                Logger.Log("Reading OBJECT_TYPE_INFORMATION->Name UNICODE_STRING");
            }

            //TEMPFIX
            ipTemp = objObjectType.Name.Buffer;

            //Read unicode string
            //Try to read unicode string
            strObjectTypeName = Marshal.PtrToStringUni(CustomAPI.Is64Bits() ? new IntPtr(ipTemp.ToInt64()) : new IntPtr(ipTemp.ToInt32()));

            if (Logger.logFileDetailQuery)
            {
                Logger.Log("\t=>{0}", strObjectTypeName);
            }

            //Free memory
            Marshal.FreeHGlobal(ipObjectType);

            //Return if this is not a file handle
            if (strObjectTypeName != "File")
            {
                //Close the duplicated handle and return
                Win32API.CloseHandle(ipHandle);
                return null;
            }

            if (Logger.logFileDetailQuery)
            {
                Logger.Log("Querying OBJECT_NAME_INFORMATION");
            }

            //Query object name information
            //Allocate memory
            nLength = objBasic.NameInformationLength;
            ipObjectName = Marshal.AllocHGlobal(nLength);

            //Query object name information, checking for length mismatch
            while ((uint)(nReturn = Win32API.NtQueryObject(ipHandle, (int)Win32API.ObjectInformationClass.ObjectNameInformation, ipObjectName, nLength, ref nLength)) == Win32API.STATUS_INFO_LENGTH_MISMATCH)
            {
                //Re-allocate memory if length mismatch
                Marshal.FreeHGlobal(ipObjectName);
                ipObjectName = Marshal.AllocHGlobal(nLength);
            }

            //Read struct
            objObjectName = (Win32API.OBJECT_NAME_INFORMATION)Marshal.PtrToStructure(ipObjectName, objObjectName.GetType());

            /*
            //Get pointer to object name UNICODE_STRING
            if (CustomAPI.Is64Bits())
            {
                ipTemp = new IntPtr(Convert.ToInt64(objObjectName.Name.Buffer.ToString(), 10) >> 32);
            }
            else
            {
                ipTemp = objObjectName.Name.Buffer;
            }
            */

            //byte[] baTemp = new byte[nLength];
            //Win32API.CopyMemory( baTemp, ipTemp, (uint)nLength );

            //TEMPFIX
            ipTemp = objObjectName.Name.Buffer;

            //Check that the string is after the process in memory
            if(ipTemp.ToInt64() < processHandle.ToInt64())
            {
                return null;
            }

            //MaximumLength should be 2 more than Length
            if(objObjectName.Name.Length != objObjectName.Name.MaximumLength - 2)
            {
                return null;
            }

            if (Logger.logFileDetailQuery)
            {
                Logger.Log("Reading OBJECT_TYPE_INFORMATION->Name UNICODE_STRING");
            }

            //Read unicode string
            strObjectName = Marshal.PtrToStringUni(CustomAPI.Is64Bits() ? new IntPtr(ipTemp.ToInt64()) : new IntPtr(ipTemp.ToInt32()));

            if (Logger.logFileDetailQuery)
            {
                Logger.Log("\t=>{0}", strObjectName);
            }

            //Free memory
            Marshal.FreeHGlobal(ipObjectName);

            //Close the duplicated handle
            Win32API.CloseHandle(ipHandle);

            //Query regular filepath with QueryDosDevice
            fd.Name = GetRegularFileNameFromDevice(strObjectName);

            if (Logger.logFileDetailQuery)
            {
                Logger.Log("Successfully queried file handle {0}", sYSTEM_HANDLE_INFORMATION.Handle);
            }

            //Return result
            return fd;
        }

        private static string GetRegularFileNameFromDevice(string strRawName)
        {
            string strFileName = strRawName;

            foreach (string strDrivePath in Environment.GetLogicalDrives())
            {
                StringBuilder sbTargetPath = new StringBuilder(Win32API.MAX_PATH);
                if (Win32API.QueryDosDevice(strDrivePath.Substring(0, 2), sbTargetPath, Win32API.MAX_PATH) == 0)
                {
                    return strRawName;
                }

                string strTargetPath = sbTargetPath.ToString();
                if (strFileName.StartsWith(strTargetPath))
                {
                    strFileName = strFileName.Replace(strTargetPath, strDrivePath.Substring(0, 2));
                    break;
                }
            }

            return strFileName;
        }
    }
}
