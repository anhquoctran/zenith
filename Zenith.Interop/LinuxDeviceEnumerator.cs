using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Zenith.Core;

namespace Zenith.Interop
{
	public class LinuxDeviceEnumerator : IDeviceEnumerator
	{
		private static readonly Regex XrandrRegex = new Regex(@"(\d+)x(\d+)\+(\d+)\+(\d+)", RegexOptions.Compiled);

		private static string RunCommand(string command, string arguments)
		{
			try
			{
				var startInfo = new ProcessStartInfo
				{
					FileName = command,
					Arguments = arguments,
					RedirectStandardOutput = true,
					UseShellExecute = false,
					CreateNoWindow = true
				};
				using var process = Process.Start(startInfo);
				if (process == null) return string.Empty;
				return process.StandardOutput.ReadToEnd();
			}
			catch
			{
				return string.Empty;
			}
		}

		public IEnumerable<AudioSource> GetAudioSources()
		{
			var sources = new List<AudioSource>();
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				return sources;

			try
			{
				var pactlOutput = RunCommand("pactl", "list short sources");
				if (!string.IsNullOrEmpty(pactlOutput))
				{
					var lines = pactlOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
					foreach (var line in lines)
					{
						var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
						if (parts.Length >= 2)
						{
							var id = parts[1];
							var name = id;
							if (name.Contains(".monitor"))
							{
								name = "System Audio (Monitor): " + name.Replace(".monitor", "");
							}
							else
							{
								name = "Microphone/Input: " + name;
							}
							sources.Add(new AudioSource { Name = name, Id = id });
						}
					}
				}

				if (sources.Count == 0 && File.Exists("/proc/asound/cards"))
				{
					var cardsContent = File.ReadAllLines("/proc/asound/cards");
					foreach (var lineContent in cardsContent)
					{
						var line = lineContent.Trim();
						if (string.IsNullOrEmpty(line)) continue;

						var match = Regex.Match(line, @"^(\d+)\s+\[([^\]]+)\]:\s+(.+)");
						if (match.Success)
						{
							var index = match.Groups[1].Value;
							var cardName = match.Groups[3].Value.Trim();
							sources.Add(new AudioSource
							{
								Name = $"ALSA Card {index}: {cardName}",
								Id = $"hw:{index}"
							});
						}
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error enumerating Linux audio sources: {ex.Message}");
			}

			return sources;
		}

		public IEnumerable<VideoSource> GetVideoSources()
		{
			var sources = new List<VideoSource>();
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				return sources;

			try
			{
				var output = RunCommand("xrandr", "--current");
				if (string.IsNullOrEmpty(output))
				{
					output = RunCommand("xrandr", "");
				}

				if (!string.IsNullOrEmpty(output))
				{
					var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
					foreach (var line in lines)
					{
						if (line.Contains(" connected "))
						{
							var match = XrandrRegex.Match(line);
							if (match.Success)
							{
								var name = line.Split(' ')[0];
								var width = int.Parse(match.Groups[1].Value);
								var height = int.Parse(match.Groups[2].Value);
								var x = int.Parse(match.Groups[3].Value);
								var y = int.Parse(match.Groups[4].Value);

								sources.Add(new VideoSource
								{
									Name = $"{name} ({width}x{height})",
									Id = name,
									Width = width,
									Height = height,
									X = x,
									Y = y,
									OwningGpuId = "Auto"
								});
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error enumerating Linux displays: {ex.Message}");
			}

			if (sources.Count == 0)
			{
				sources.Add(new VideoSource { Name = "Display 1", Id = ":0.0", Width = 1920, Height = 1080, OwningGpuId = "Auto" });
			}
			return sources;
		}

		public IEnumerable<GPUDevice> GetGPUDevices()
		{
			var gpus = new List<GPUDevice>();
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				return gpus;

			try
			{
				if (Directory.Exists("/sys/class/drm"))
				{
					foreach (var dir in Directory.GetDirectories("/sys/class/drm"))
					{
						var name = Path.GetFileName(dir);
						if (name.StartsWith("renderD"))
						{
							string gpuName = $"DRM Render Node ({name})";
							var vendorFile = Path.Combine(dir, "device", "vendor");
							var deviceFile = Path.Combine(dir, "device", "device");
							if (File.Exists(vendorFile) && File.Exists(deviceFile))
							{
								var vendor = File.ReadAllText(vendorFile).Trim();
								string vendorFriendly = vendor;
								if (vendor.Contains("0x8086")) vendorFriendly = "Intel";
								else if (vendor.Contains("0x10de")) vendorFriendly = "NVIDIA";
								else if (vendor.Contains("0x1002")) vendorFriendly = "AMD";
								
								gpuName = $"{vendorFriendly} GPU ({name})";
							}
							gpus.Add(new GPUDevice
							{
								Name = gpuName,
								Id = $"/dev/dri/{name}"
							});
						}
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error enumerating Linux GPUs: {ex.Message}");
			}
			return gpus;
		}

		public IEnumerable<WebcamSource> GetWebcams()
		{
			var sources = new List<WebcamSource>();
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				return sources;

			try
			{
				if (Directory.Exists("/sys/class/video4linux"))
				{
					foreach (var dir in Directory.GetDirectories("/sys/class/video4linux"))
					{
						var nameFile = Path.Combine(dir, "name");
						if (File.Exists(nameFile))
						{
							var name = File.ReadAllText(nameFile).Trim();
							var devName = Path.GetFileName(dir);
							var id = $"/dev/{devName}";
							sources.Add(new WebcamSource { Name = name, Id = id });
						}
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error enumerating Linux webcams: {ex.Message}");
			}
			return sources;
		}
	}
}
