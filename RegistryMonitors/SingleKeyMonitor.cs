using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace RegistryMonitors
{
    public class SingleKeyMonitor : IDisposable
    {
        private IntPtr _registryKeyHandle = IntPtr.Zero;
        private Thread _monitorThread;
        private volatile bool _stopRequested;

        private readonly string _fullSubKeyPath;
        private readonly string _valueName;

        public event EventHandler<long?> ValueChanged;

        #region Win32 P/Invokes

        private static readonly IntPtr HKEY_CURRENT_USER = new IntPtr(unchecked((int)0x80000001));

        private const int KEY_NOTIFY = 0x0010;
        private const int KEY_QUERY_VALUE = 0x0001;
        private const int ERROR_SUCCESS = 0;

        private const int REG_NOTIFY_CHANGE_LAST_SET = 0x0004;

        private const uint REG_DWORD = 4;
        private const uint REG_QWORD = 11;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegOpenKeyEx(
            IntPtr hKey,
            string lpSubKey,
            int ulOptions,
            int samDesired,
            out IntPtr phkResult);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegCloseKey(IntPtr hKey);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegNotifyChangeKeyValue(
            IntPtr hKey,
            bool bWatchSubtree,
            int dwNotifyFilter,
            IntPtr hEvent,
            bool fAsynchronous);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegQueryValueEx(
            IntPtr hKey,
            string lpValueName,
            IntPtr lpReserved,
            out uint lpType,
            byte[] lpData,
            ref uint lpcbData);

        #endregion

        public SingleKeyMonitor(string fullSubKeyPath, string valueName)
        {
            _fullSubKeyPath = fullSubKeyPath;
            _valueName = valueName;
        }

        public void Start()
        {
            if (_monitorThread != null && _monitorThread.IsAlive)
            {
                return; // Already started
            }

            // Attempt to open the subkey for notify + read
            int openResult = RegOpenKeyEx(
                HKEY_CURRENT_USER,
                _fullSubKeyPath,
                0,
                KEY_NOTIFY | KEY_QUERY_VALUE,
                out _registryKeyHandle);

            if (openResult != ERROR_SUCCESS)
            {
                throw new ApplicationException($"Failed to open subkey: {_fullSubKeyPath}, Error={openResult}");
            }

            _stopRequested = false;
            _monitorThread = new Thread(MonitorLoop)
            {
                IsBackground = true
            };
            _monitorThread.Start();
        }

        public void Stop()
        {
            _stopRequested = true;
            if (_registryKeyHandle != IntPtr.Zero)
            {
                RegCloseKey(_registryKeyHandle);
                _registryKeyHandle = IntPtr.Zero;
            }

            if (_monitorThread != null && _monitorThread.IsAlive)
            {
                _monitorThread.Join();
            }
            _monitorThread = null;
        }

        private void MonitorLoop()
        {
            try
            {
                while (!_stopRequested && _registryKeyHandle != IntPtr.Zero)
                {
                    // Wait for the key to change
                    int ret = RegNotifyChangeKeyValue(
                        _registryKeyHandle,
                        false, // bWatchSubtree
                        REG_NOTIFY_CHANGE_LAST_SET,
                        IntPtr.Zero, // hEvent
                        false);      // fAsynchronous => this call blocks

                    if (_stopRequested || _registryKeyHandle == IntPtr.Zero)
                        break;

                    if (ret == ERROR_SUCCESS)
                    {
                        // The key's values changed; read the new value
                        var newValue = ReadValue(_valueName);
                        OnValueChanged(newValue);
                    }
                    else
                    {
                        // Some error occurred or key was closed
                        break;
                    }
                }
            }
            catch
            {
                // Logging or handle the exception as needed
            }
        }

        private long? ReadValue(string valueName)
        {
            if (_registryKeyHandle == IntPtr.Zero) return null;

            uint type;
            uint dataSize = 0;
            int result = RegQueryValueEx(_registryKeyHandle, valueName, IntPtr.Zero, out type, null, ref dataSize);
            if (result != ERROR_SUCCESS || dataSize == 0)
                return null;

            byte[] data = new byte[dataSize];
            result = RegQueryValueEx(_registryKeyHandle, valueName, IntPtr.Zero, out type, data, ref dataSize);
            if (result != ERROR_SUCCESS)
                return null;

            // Interpret the data

            if (type == REG_DWORD && data.Length >= 4)
            {
                // 32-bit integer
                int dwordValue = BitConverter.ToInt32(data, 0);
                return dwordValue;
            }
            else
            {
                if (type == REG_QWORD && data.Length >= 8)
                {
                    long qwordValue = BitConverter.ToInt64(data, 0);
                    return qwordValue;
                }
            }

            return null;
        }

        private void OnValueChanged(long? newValue)
        {
            ValueChanged?.Invoke(this, newValue);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
