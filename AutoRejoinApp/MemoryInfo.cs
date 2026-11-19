using System.Runtime.InteropServices;

namespace AutoRejoinApp;

public class MemoryInfo
{
	private struct MEMORYSTATUSEX
	{
		public uint dwLength;

		public uint dwMemoryLoad;

		public ulong ullTotalPhys;

		public ulong ullAvailPhys;

		public ulong ullTotalPageFile;

		public ulong ullAvailPageFile;

		public ulong ullTotalVirtual;

		public ulong ullAvailVirtual;

		public ulong ullAvailExtendedVirtual;
	}

	public double FreeCommitGB;

	public double CommitLimitGB;

	public double TotalRamGB;

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

	public static MemoryInfo Get()
	{
		MEMORYSTATUSEX buffer = new MEMORYSTATUSEX
		{
			dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX))
		};
		if (!GlobalMemoryStatusEx(ref buffer))
		{
			return null;
		}
		MemoryInfo memoryInfo = new MemoryInfo();
		memoryInfo.FreeCommitGB = (double)buffer.ullAvailPageFile / 1073741824.0;
		memoryInfo.CommitLimitGB = (double)buffer.ullTotalPageFile / 1073741824.0;
		memoryInfo.TotalRamGB = (double)buffer.ullTotalPhys / 1073741824.0;
		return memoryInfo;
	}
}
