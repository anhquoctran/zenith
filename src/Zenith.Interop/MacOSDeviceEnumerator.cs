using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Zenith.Core;

namespace Zenith.Interop
{
	public class MacOSDeviceEnumerator : IDeviceEnumerator
	{
		[DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
		private static extern IntPtr objc_getClass(string className);

		[DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
		private static extern IntPtr sel_registerName(string selectorName);

		[DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
		private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

		[DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
		private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

		[DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
		private static extern IntPtr objc_msgSend_int(IntPtr receiver, IntPtr selector, int arg);

		[StructLayout(LayoutKind.Sequential)]
		private struct CGPoint
		{
			public double X;
			public double Y;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct CGSize
		{
			public double Width;
			public double Height;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct CGRect
		{
			public CGPoint Origin;
			public CGSize Size;
		}

		[DllImport("CoreGraphics", EntryPoint = "CGGetActiveDisplayList")]
		private static extern int CGGetActiveDisplayList(uint maxDisplays, uint[] activeDisplays, out uint displayCount);

		[DllImport("CoreGraphics", EntryPoint = "CGDisplayBounds")]
		private static extern CGRect CGDisplayBounds(uint display);

		[DllImport("/System/Library/Frameworks/Metal.framework/Metal")]
		private static extern IntPtr MTLCopyAllDevices();

		private struct AVDeviceDescription
		{
			public string Name;
			public string UniqueID;
		}

		private static string GetNSString(IntPtr nsString)
		{
			if (nsString == IntPtr.Zero) return string.Empty;
			var sel_UTF8String = sel_registerName("UTF8String");
			var ptr = objc_msgSend(nsString, sel_UTF8String);
			return Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
		}

		private static IntPtr CreateNSString(string str)
		{
			var cls_NSString = objc_getClass("NSString");
			var sel_stringWithUTF8String = sel_registerName("stringWithUTF8String:");
			var utf8Ptr = Marshal.StringToCoTaskMemUTF8(str);
			try
			{
				return objc_msgSend_IntPtr(cls_NSString, sel_stringWithUTF8String, utf8Ptr);
			}
			finally
			{
				Marshal.FreeCoTaskMem(utf8Ptr);
			}
		}

		private static IEnumerable<AVDeviceDescription> GetAVCaptureDevices(string mediaType)
		{
			var list = new List<AVDeviceDescription>();
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				return list;

			try
			{
				var cls_AVCaptureDevice = objc_getClass("AVCaptureDevice");
				if (cls_AVCaptureDevice == IntPtr.Zero) return list;

				var sel_devicesWithMediaType = sel_registerName("devicesWithMediaType:");
				var mediaTypeNs = CreateNSString(mediaType);
				var array_devices = objc_msgSend_IntPtr(cls_AVCaptureDevice, sel_devicesWithMediaType, mediaTypeNs);
				if (array_devices == IntPtr.Zero) return list;

				var sel_count = sel_registerName("count");
				var count = (int)objc_msgSend(array_devices, sel_count);

				var sel_objectAtIndex = sel_registerName("objectAtIndex:");
				var sel_localizedName = sel_registerName("localizedName");
				var sel_uniqueID = sel_registerName("uniqueID");

				for (var i = 0; i < count; i++)
				{
					var device = objc_msgSend_int(array_devices, sel_objectAtIndex, i);
					if (device == IntPtr.Zero) continue;

					var nameNs = objc_msgSend(device, sel_localizedName);
					var uidNs = objc_msgSend(device, sel_uniqueID);

					var name = GetNSString(nameNs);
					var uid = GetNSString(uidNs);

					list.Add(new AVDeviceDescription { Name = name, UniqueID = uid });
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error enumerating AVDevices ({mediaType}): {ex.Message}");
			}
			return list;
		}

		public IEnumerable<AudioSource> GetAudioSources()
		{
			var sources = new List<AudioSource>();
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				return sources;

			var devices = GetAVCaptureDevices("soun");
			foreach (var dev in devices)
			{
				sources.Add(new AudioSource { Name = dev.Name, Id = dev.UniqueID });
			}
			return sources;
		}

		public IEnumerable<VideoSource> GetVideoSources()
		{
			var sources = new List<VideoSource>();
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				return sources;

			string defaultGpuId = "Auto";
			var gpus = new List<GPUDevice>(GetGPUDevices());
			if (gpus.Count > 0)
			{
				defaultGpuId = gpus[0].Id;
			}

			try
			{
				uint maxDisplays = 32;
				var activeDisplays = new uint[maxDisplays];
				var result = CGGetActiveDisplayList(maxDisplays, activeDisplays, out var displayCount);
				if (result == 0 && displayCount > 0)
				{
					for (var i = 0; i < displayCount; i++)
					{
						var displayId = activeDisplays[i];
						var bounds = CGDisplayBounds(displayId);
						var width = (int)bounds.Size.Width;
						var height = (int)bounds.Size.Height;
						var x = (int)bounds.Origin.X;
						var y = (int)bounds.Origin.Y;

						sources.Add(new VideoSource
						{
							Name = $"Screen {sources.Count + 1} ({width}x{height})",
							Id = displayId.ToString(),
							Width = width,
							Height = height,
							X = x,
							Y = y,
							OwningGpuId = defaultGpuId
						});
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error enumerating macOS displays: {ex.Message}");
			}

			if (sources.Count == 0)
			{
				sources.Add(new VideoSource { Name = "Display 1", Id = "1", Width = 1920, Height = 1080, OwningGpuId = defaultGpuId });
			}
			return sources;
		}

		public IEnumerable<GPUDevice> GetGPUDevices()
		{
			var gpus = new List<GPUDevice>();
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				return gpus;

			try
			{
				IntPtr array_devices = MTLCopyAllDevices();
				if (array_devices == IntPtr.Zero) return gpus;

				IntPtr sel_count = sel_registerName("count");
				int count = (int)objc_msgSend(array_devices, sel_count);

				IntPtr sel_objectAtIndex = sel_registerName("objectAtIndex:");
				IntPtr sel_name = sel_registerName("name");

				for (int i = 0; i < count; i++)
				{
					IntPtr device = objc_msgSend_int(array_devices, sel_objectAtIndex, i);
					if (device == IntPtr.Zero) continue;

					IntPtr nameNs = objc_msgSend(device, sel_name);
					string name = GetNSString(nameNs);

					gpus.Add(new GPUDevice
					{
						Name = name,
						Id = name
					});
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error enumerating Metal GPUs: {ex.Message}");
			}
			return gpus;
		}

		public IEnumerable<WebcamSource> GetWebcams()
		{
			var sources = new List<WebcamSource>();
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				return sources;

			var devices = GetAVCaptureDevices("vide");
			foreach (var dev in devices)
			{
				sources.Add(new WebcamSource { Name = dev.Name, Id = dev.UniqueID });
			}
			return sources;
		}
	}
}
