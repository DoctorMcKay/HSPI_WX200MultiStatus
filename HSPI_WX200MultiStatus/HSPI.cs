using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Timers;
using HomeSeer.Jui.Views;
using HomeSeer.PluginSdk;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Logging;
using HSPI_WX200MultiStatus.DataStructures;
using HSPI_WX200MultiStatus.Enums;

namespace HSPI_WX200MultiStatus {
	// ReSharper disable once InconsistentNaming
	public class HSPI : AbstractPlugin, IGetDevicesActionListener {
		public override string Name { get; } = "HS-WX200 Multi-Status";
		public override string Id { get; } = "WX200MultiStatus";
		public override bool SupportsConfigDeviceAll { get; } = true;

		private readonly List<WX200Device> _devices;
		internal byte WsBlinkFrequency;
		private bool _debugLogging;

		public HSPI() {
			_devices = new List<WX200Device>();

#if DEBUG
			LogDebug = true;
#endif
		}

		protected override void Initialize() {
			WriteLog(ELogType.Debug, "Initialize");
			
			AnalyticsClient analytics = new AnalyticsClient(this, HomeSeerSystem);

			WsBlinkFrequency = byte.Parse(HomeSeerSystem.GetINISetting("Options", "ws_blink_frequency", "5", SettingsFileName));
			List<string> blinkFreqOptions = new List<string>();
			for (int i = 1; i <= 255; i++) {
				blinkFreqOptions.Add((double) i / 10 + " seconds on, " + (double) i / 10 + " seconds off");
			}

			// Build the settings page
			PageFactory settingsPageFactory = PageFactory
				.CreateSettingsPage("WX200MultiStatusSettings", "WX200 Multi Status Settings")
				.WithLabel("plugin_status", "Status (refresh to update)", "x")
				.WithDropDownSelectList("ws_blink_frequency", "HS-WS200+ Blink Frequency", blinkFreqOptions, WsBlinkFrequency - 1)
				.WithGroup("debug_group", "<hr>", new AbstractView[] {
					new LabelView("debug_support_link", "Documentation", "<a href=\"https://github.com/DoctorMcKay/HSPI_WX200MultiStatus/blob/master/README.md\" target=\"_blank\">GitHub</a>"), 
					new LabelView("debug_system_id", "System ID (include this with any support requests)", analytics.CustomSystemId),
#if DEBUG
					new LabelView("debug_log", "Enable Debug Logging", "ON - DEBUG BUILD")
#else
				new ToggleView("debug_log", "Enable Debug Logging")
#endif
				});

			Settings.Add(settingsPageFactory.Page);
			ActionTypes.AddActionType(typeof(StatusLedAction));

			Status = PluginStatus.Info("Initializing...");

			WriteLog(ELogType.Trace, "Enumerating HS devices to find WX200 devices");
			string deviceList = "";
			DateTime start = DateTime.Now;
			foreach (HsDevice device in HomeSeerSystem.GetAllDevices(false).Where(WX200Device.IsWX200Device)) {
				WX200Device wxDevice = new WX200Device(this, device);
				_devices.Add(wxDevice);
				deviceList += (deviceList.Length > 0 ? ", " : "") + $"{wxDevice.HomeId}:{wxDevice.NodeId}";
			}

			double time = DateTime.Now.Subtract(start).TotalMilliseconds;
			WriteLog(ELogType.Info, $"Initialization complete in {time} ms. Found {_devices.Count} WX200 devices: {deviceList}");
			Status = PluginStatus.Ok();
			analytics.ReportIn(5000);
			
			Timer timer = new Timer(20000) {Enabled = true, AutoReset = false};
			timer.Elapsed += async (src, arg) => {
				WriteLog(ELogType.Info, "Synchronizing all WX200 device states");
				DateTime start2 = DateTime.Now;
				double waitTime = 0;
				foreach (WX200Device device in _devices) {
					DateTime start3 = DateTime.Now;
					device.SyncState();
					waitTime += DateTime.Now.Subtract(start3).TotalMilliseconds;
					await Task.Delay(2000);
				}
				WriteLog(ELogType.Info, $"Device states synchronized in {DateTime.Now.Subtract(start2).TotalMilliseconds} ms ({waitTime} ms waiting)");
			};
		}
		
		protected override void OnSettingsLoad() {
			// Called when the settings page is loaded. Use to pre-fill the inputs.
			string statusText = Status.Status.ToString().ToUpper();
			if (Status.StatusText.Length > 0) {
				statusText += ": " + Status.StatusText;
			}
			((LabelView) Settings.Pages[0].GetViewById("plugin_status")).Value = statusText;
			((SelectListView) Settings.Pages[0].GetViewById("ws_blink_frequency")).Selection = WsBlinkFrequency - 1;
		}
		
		protected override bool OnSettingChange(string pageId, AbstractView currentView, AbstractView changedView) {
			WriteLog(ELogType.Debug, $"Request to save setting {currentView.Id} on page {pageId}");

			if (pageId != "WX200MultiStatusSettings") {
				WriteLog(ELogType.Warning, $"Request to save settings on unknown page {pageId}!");
				return true;
			}

			switch (currentView.Id) {
				case "ws_blink_frequency":
					WsBlinkFrequency = (byte) (byte.Parse(changedView.GetStringValue()) + 1);
					HomeSeerSystem.SaveINISetting("Options", "ws_blink_frequency", WsBlinkFrequency.ToString(), SettingsFileName);
					WriteLog(ELogType.Info, $"WS200 blink frequency set to {WsBlinkFrequency}");
					return true;
				
				case "debug_log":
					_debugLogging = changedView.GetStringValue() == "True";
					return true;
			}
			
			WriteLog(ELogType.Info, $"Request to save unknown setting {currentView.Id}");
			return false;
		}

		public override bool HasJuiDeviceConfigPage(int deviceRef) {
			if (_devices.Any(device => device.DevRef == deviceRef)) {
				WriteLog(ELogType.Trace, $"Reporting that we have a device config page for {deviceRef}");
				return true;
			}

			WriteLog(ELogType.Trace, $"Reporting that we do NOT have a device config page for {deviceRef}");
			return false;
		}

		public override string GetJuiDeviceConfigPage(int deviceRef) {
			WriteLog(ELogType.Trace, $"Device config page requested for {deviceRef}");
			WX200Device device = _devices.Find(dev => dev.DevRef == deviceRef);

			PageFactory factory = PageFactory.CreateDeviceConfigPage("WX200MultiStatusDevice", "WX200 Multi-Status")
				.WithLabel("AddToGroupLabel", "To add this device to a group, type the name of the desired group. The group will be created if it does not exist.")
				.WithInput("AddToGroup", "Add To Group");

			if (device.Groups.Count > 0) {
				factory.WithDropDownSelectList("RemoveFromGroup", "Remove From Group", device.Groups);
			}

			return factory.Page.ToJsonString();
		}

		protected override bool OnDeviceConfigChange(Page deviceConfigPage, int deviceRef) {
			string addGroup = "";
			int removeGroup = -1;
			
			if (deviceConfigPage.ContainsViewWithId("AddToGroup")) {
				addGroup = deviceConfigPage.GetViewById("AddToGroup").GetStringValue();
			}
			if (deviceConfigPage.ContainsViewWithId("RemoveFromGroup")) {
				int.TryParse(deviceConfigPage.GetViewById("RemoveFromGroup").GetStringValue(), out removeGroup);
			}

			WX200Device device = _devices.Find(dev => dev.DevRef == deviceRef);
			bool shouldSave = false;

			if (!string.IsNullOrEmpty(addGroup)) {
				WriteLog(ELogType.Info, $"Adding device {deviceRef} to group {addGroup}");
				device.Groups.Add(addGroup);
				shouldSave = true;
			}

			if (removeGroup != -1) {
				WriteLog(ELogType.Info, $"Removing device {deviceRef} from group {removeGroup} ({device.Groups[removeGroup]})");
				device.Groups.RemoveAt(removeGroup);
				shouldSave = true;
			}

			if (shouldSave) {
				device.SaveGroups();
			}

			return true;
		}

		internal int ConfigGet(string homeId, byte nodeId, byte configProperty) {
			DateTime start = DateTime.Now;
			int result = (int) HomeSeerSystem.LegacyPluginFunction("Z-Wave", "", "Configuration_Get", new object[] {
				homeId,
				nodeId,
				configProperty
			});

			double ms = DateTime.Now.Subtract(start).TotalMilliseconds;
			if (ms > 2000) {
				WriteLog(ELogType.Warning, $"Node {homeId}:{nodeId} was very slow to respond ({ms} ms) and might need to be optimized.");
			}
			WriteLog(ELogType.Debug, $"Retrieved {homeId}:{nodeId}:{(WX200ConfigParam) configProperty} = {result} in {ms} ms");
			return result;
		}

		internal ConfigResult ConfigSet(string homeId, byte nodeId, byte configProperty, byte valueLength, int value) {
			DateTime start = DateTime.Now;
			ConfigResult result = (ConfigResult) HomeSeerSystem.LegacyPluginFunction("Z-Wave", "", "Configuration_Set", new object[] {
				homeId,
				nodeId,
				configProperty,
				valueLength,
				value
			});
			
			double ms = DateTime.Now.Subtract(start).TotalMilliseconds;
			if (ms > 2000) {
				WriteLog(ELogType.Warning, $"Node {homeId}:{nodeId} was very slow to respond ({ms} ms) and might need to be optimized.");
			}
			WriteLog(ELogType.Debug, $"Set {homeId}:{nodeId}:{(WX200ConfigParam) configProperty} = {value}:{valueLength} with result {result} in {ms} ms");
			return result;
		}

		internal Dictionary<string, string> GetIniSection(string section) {
			return HomeSeerSystem.GetIniSection(section, SettingsFileName);
		}

		internal void ClearIniSection(string section) {
			HomeSeerSystem.ClearIniSection(section, SettingsFileName);
		}

		internal void SaveIniSetting(string section, string key, string value) {
			HomeSeerSystem.SaveINISetting(section, key, value, SettingsFileName);
		}

		public Dictionary<string, WX200Device> GetDevices() {
			Dictionary<string, WX200Device> output = new Dictionary<string, WX200Device>();
			foreach (WX200Device device in _devices) {
				AbstractHsDevice hsDevice = HomeSeerSystem.GetDeviceByRef(device.DevRef);
				string name = $"{hsDevice.Location2} {hsDevice.Location} {hsDevice.Name}";
				output.Add(name, device);
			}

			return output;
		}

		public DeviceCollection GetDeviceCollection(string filter) {
			WriteLog(ELogType.Trace, $"Building device collection from filter \"{filter}\"");
			
			if (filter == "__all") {
				return new DeviceCollection(_devices);
			}

			if (filter.StartsWith("_")) {
				string group = filter.Substring(1);
				return new DeviceCollection(_devices.FindAll(device => device.Groups.Contains(group)));
			}
			
			// If it's not __all or a _something, then it's a device ref
			if (!int.TryParse(filter, out int devRef)) {
				throw new Exception("Bad input");
			}
			
			return new DeviceCollection(_devices.Find(device => device.DevRef == devRef));
		}

		public IEnumerable<string> GetDeviceGroups() {
			List<string> output = new List<string>();
			foreach (WX200Device device in _devices) {
				foreach (string group in device.Groups) {
					if (!output.Contains(group)) {
						output.Add(group);
					}
				}
			}
			
			output.Sort();
			return output;
		}

		protected override void BeforeReturnStatus() { }
		
		public void WriteLog(ELogType logType, string message, [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string caller = null) {
#if DEBUG
			bool isDebugMode = true;

			// Prepend calling function and line number
			message = $"[{caller}:{lineNumber}] {message}";
			
			// Also print to console in debug builds
			string type = logType.ToString().ToLower();
			Console.WriteLine($"[{type}] {message}");
#else
			if (logType == ELogType.Trace) {
				// Don't record Trace events in production builds even if debug logging is enabled
				return;
			}

			bool isDebugMode = _debugLogging;
#endif

			if (logType <= ELogType.Debug && !isDebugMode) {
				return;
			}
			
			HomeSeerSystem.WriteLog(logType, message, Name);
		}
	}
}
