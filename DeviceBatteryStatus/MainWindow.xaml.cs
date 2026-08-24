using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace DeviceBatteryStatus
{
    public partial class MainWindow : Window
    {
        const int PROCESS_VM_READ = 0x0010;

        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr hObject);

        private DispatcherTimer _timer;

        public MainWindow()
        {
            InitializeComponent();

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(2);
            _timer.Tick += (s, e) => ReadBatteryFromMemory();
            _timer.Start();

            ReadBatteryFromMemory();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void ReadBatteryFromMemory()
        {
            Process process = Process.GetProcessesByName("Dark Project Nexus").FirstOrDefault();

            if (process == null)
            {
                System.Diagnostics.Debug.WriteLine("Aplikacja Dark Project Nexus nie działa w tle!");
                return;
            }

            ProcessModule guiModule = null;
            ProcessModule coreModule = null;

            foreach (ProcessModule m in process.Modules)
            {
                if (m.ModuleName.Equals("Qt5Gui.dll", StringComparison.OrdinalIgnoreCase))
                {
                    guiModule = m;
                }
                else if (m.ModuleName.Equals("Qt5Core.dll", StringComparison.OrdinalIgnoreCase))
                {
                    coreModule = m;
                }
            }

            if (guiModule == null)
            {
                System.Diagnostics.Debug.WriteLine("Nie znaleziono modułu Qt5Gui.dll.");
                return;
            }

            IntPtr processHandle = OpenProcess(PROCESS_VM_READ, false, process.Id);
            if (processHandle == IntPtr.Zero) return;

            try
            {
                IntPtr baseAddress = guiModule.BaseAddress + 0x0058D080;
                int[] offsets = { 0x10, 0x30, 0x90, 0x30, 0x58, 0x28, 0xA0 };
                IntPtr currentAddress = baseAddress;

                for (int i = 0; i < offsets.Length; i++)
                {
                    byte[] pointerBuffer = new byte[8];
                    ReadProcessMemory(processHandle, currentAddress, pointerBuffer, pointerBuffer.Length, out _);
                    long nextAddress = BitConverter.ToInt64(pointerBuffer, 0);

                    currentAddress = new IntPtr(nextAddress + offsets[i]);
                }

                byte[] valueBuffer = new byte[4];
                ReadProcessMemory(processHandle, currentAddress, valueBuffer, valueBuffer.Length, out _);
                int batteryLevel = BitConverter.ToInt32(valueBuffer, 0);

                int isCharging = 0;
                if (coreModule != null)
                {
                    IntPtr chargeAddress = coreModule.BaseAddress + 0x0055DEE8;
                    int[] chargeOffsets = { 0x2C8, 0x8, 0x60, 0x8, 0x80, 0x28, 0x8 };
                    IntPtr currentChargeAddress = chargeAddress;

                    for (int i = 0; i < chargeOffsets.Length; i++)
                    {
                        byte[] pointerBuffer = new byte[8];
                        ReadProcessMemory(processHandle, currentChargeAddress, pointerBuffer, pointerBuffer.Length, out _);
                        long nextAddress = BitConverter.ToInt64(pointerBuffer, 0);

                        currentChargeAddress = new IntPtr(nextAddress + chargeOffsets[i]);
                    }

                    byte[] chargeBuffer = new byte[4];
                    ReadProcessMemory(processHandle, currentChargeAddress, chargeBuffer, chargeBuffer.Length, out _);
                    isCharging = BitConverter.ToInt32(chargeBuffer, 0);
                }

                Dispatcher.Invoke(() =>
                {
                    if (batteryLevel >= 0 && batteryLevel <= 100)
                    {
                        BatteryText.Text = $"{batteryLevel}%";
                        BatteryFill.Width = (batteryLevel / 100.0) * 46.0;
                    }

                    if (isCharging == 1)
                    {
                        ChargingIcon.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        ChargingIcon.Visibility = Visibility.Hidden;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd odczytu pamięci: {ex.Message}");
            }
            finally
            {
                CloseHandle(processHandle);
            }
        }
    }
}