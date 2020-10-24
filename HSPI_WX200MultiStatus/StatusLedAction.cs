using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using HomeSeer.Jui.Views;
using HomeSeer.PluginSdk.Devices;
using HomeSeer.PluginSdk.Events;
using HomeSeer.PluginSdk.Logging;
using HSPI_WX200MultiStatus.DataStructures;
using HSPI_WX200MultiStatus.Enums;

namespace HSPI_WX200MultiStatus {
	public class StatusLedAction : AbstractActionType {
		private IGetDevicesActionListener Listener => ActionListener as IGetDevicesActionListener;
		
		private string InputIdDeviceSelect => $"{PageId}_Device";
		private string InputIdDeviceCountLabel => $"{PageId}_DeviceCount";
		private string InputIdWhichLed => $"{PageId}_LedIndex";
		private string InputIdDeviceUnsupportingCountLabel => $"{PageId}_UnsupportingDeviceCount";
		private string InputIdLedColor => $"{PageId}_LedColor";
		private string InputIdLedBlink => $"{PageId}_LedBlink";

		public StatusLedAction() { }

		public StatusLedAction(int id, int eventRef, byte[] dataIn, ActionTypeCollection.IActionTypeListener listener, bool logDebug = false)
			: base(id, eventRef, dataIn, listener, logDebug) { }

		protected override string GetName() {
			return "Z-Wave: Set HS-WX200+ Status Mode LED";
		}

		protected override void OnNewAction() {
			Console.WriteLine("NewAction");
			ConfigPage = _initNewConfigPage().Page;
		}

		public override bool IsFullyConfigured() {
			return !string.IsNullOrEmpty(_getFieldValue(InputIdDeviceSelect))
			       && !string.IsNullOrEmpty(_getFieldValue(InputIdWhichLed))
			       && !string.IsNullOrEmpty(_getFieldValue(InputIdLedColor))
			       && (
				       _getFieldValue(InputIdLedColor) == ((byte) WX200StatusModeColor.Off).ToString()
			           || !string.IsNullOrEmpty(_getFieldValue(InputIdLedBlink))
				   );
		}

		protected override bool OnConfigItemUpdate(AbstractView configViewChange) {
			// Update the selection in our config page so that _getFieldValue will work in our factory methods
			ConfigPage.UpdateViewById(configViewChange);

			if (configViewChange.Id == InputIdDeviceSelect) {
				if (_getDeviceCollection().GetMaxLedCount() == 1) {
					ConfigPage = _initConfigPageWithColor().Page;
					((SelectListView) ConfigPage.GetViewById(InputIdWhichLed)).Selection = 0;
				} else {
					ConfigPage = _initConfigPageWithLedIndex().Page;
				}
			} else if (
				configViewChange.Id == InputIdWhichLed ||
				(configViewChange.Id == InputIdLedColor && _getFieldValue(InputIdLedColor) == ((byte) WX200StatusModeColor.Off).ToString())
			) {
				ConfigPage = _initConfigPageWithColor().Page;
			} else if (configViewChange.Id == InputIdLedColor && _getFieldValue(InputIdLedColor) != ((byte) WX200StatusModeColor.Off).ToString()) {
				ConfigPage = _initConfigPageWithBlink().Page;
			}

			// We can return false here since we already did ConfigPage.UpdateViewById, which is all returning true here does
			return false;
		}

		public override string GetPrettyString() {
			StringBuilder builder = new StringBuilder();
			builder.Append("Set ");
			builder.Append(_getFieldDisplayValue(InputIdDeviceSelect));
			if (_getFieldDisplayValue(InputIdWhichLed) == "All") {
				builder.Append(" ALL LEDS");
			} else {
				builder.Append(" LED #");
				builder.Append(_getFieldDisplayValue(InputIdWhichLed));
			}
			builder.Append(" to ");
			builder.Append(_getFieldValue(InputIdLedBlink) == "1" ? "blinking " : "");
			builder.Append(_getFieldDisplayValue(InputIdLedColor));
			return builder.ToString();
		}

		public override bool OnRunAction() {
			string which = _getFieldValue(InputIdWhichLed);
			_getDeviceCollection().SetStatusLed(
				(byte) (which == "All" ? 255 : byte.Parse(which) - 1),
				(WX200StatusModeColor) byte.Parse(_getFieldValue(InputIdLedColor)),
				_getFieldValue(InputIdLedBlink) == "1"
			);

			return true;
		}

		public override bool ReferencesDeviceOrFeature(int devOrFeatRef) {
			return _getDeviceCollection().ContainsDevice(devOrFeatRef);
		}

		private int _getFieldSelection(string fieldId) {
			return !ConfigPage.ContainsViewWithId(fieldId) ? -1 : ((SelectListView) ConfigPage.GetViewById(fieldId)).Selection;
		}

		private string _getFieldValue(string fieldId) {
			return !ConfigPage.ContainsViewWithId(fieldId) ? "" : ((SelectListView) ConfigPage.GetViewById(fieldId)).GetSelectedOptionKey();
		}
		
		private string _getFieldDisplayValue(string fieldId) {
			return !ConfigPage.ContainsViewWithId(fieldId) ? "" : ((SelectListView) ConfigPage.GetViewById(fieldId)).GetSelectedOption();
		}

		private DeviceCollection _getDeviceCollection() {
			return Listener.GetDeviceCollection(_getFieldValue(InputIdDeviceSelect));
		}

		private PageFactory _initNewConfigPage() {
			List<string> deviceListOptions = new List<string> {"(All)"};
			List<string> deviceListOptionsKeys = new List<string> {"__all"};

			foreach (string group in Listener.GetDeviceGroups()) {
				deviceListOptions.Add("Group: " + group);
				deviceListOptionsKeys.Add("_" + group);
			}

			Dictionary<string, WX200Device> deviceList = Listener.GetDevices();
			List<string> deviceNames = new List<string>(deviceList.Keys);
			deviceNames.Sort();

			foreach (string deviceName in deviceNames) {
				deviceListOptions.Add(deviceName);
				deviceListOptionsKeys.Add(deviceList[deviceName].DevRef.ToString());
			}
			
			return PageFactory.CreateEventActionPage(PageId, "Action")
				.WithDropDownSelectList(InputIdDeviceSelect, "Device", deviceListOptions, deviceListOptionsKeys, _getFieldSelection(InputIdDeviceSelect));
		}

		private PageFactory _initConfigPageWithLedIndex() {
			List<string> ledListOptions = new List<string>();

			DeviceCollection deviceCollection = _getDeviceCollection();
			byte maxLedCount = deviceCollection.GetMaxLedCount();
			if (maxLedCount > 1) {
				ledListOptions.Add("All");
			}
			for (byte i = 1; i <= maxLedCount; i++) {
				ledListOptions.Add(i.ToString());
			}
			
			PageFactory factory = _initNewConfigPage();
			if (_getFieldValue(InputIdDeviceSelect).StartsWith("_")) {
				int count = deviceCollection.Count;
				factory.WithLabel(InputIdDeviceCountLabel, count + " device" + (count == 1 ? "" : "s") + " in group");
			}

			int whichLed = _getFieldSelection(InputIdWhichLed);
			factory.WithDropDownSelectList(InputIdWhichLed, "LED (position from bottom)", ledListOptions, ledListOptions, whichLed);

			if (whichLed != -1) {
				int numUnsupportingDevices = deviceCollection.GetNumDevicesNotSupportingLed((byte) whichLed);
				if (numUnsupportingDevices > 0) {
					factory.WithLabel(InputIdDeviceUnsupportingCountLabel, numUnsupportingDevices + " device" + (numUnsupportingDevices == 1 ? "" : "s") + " do not support the selected LED and will not be affected");
				}
			}

			return factory;
		}

		private PageFactory _initConfigPageWithColor() {
			List<string> colorOptions = new List<string>();
			List<string> colorOptionsKeys = new List<string>();

			foreach (WX200StatusModeColor color in Enum.GetValues(typeof(WX200StatusModeColor))) {
				colorOptions.Add(color.ToString());
				colorOptionsKeys.Add(((byte) color).ToString());
			}

			return _initConfigPageWithLedIndex()
				.WithDropDownSelectList(InputIdLedColor, "Color", colorOptions, colorOptionsKeys, _getFieldSelection(InputIdLedColor));
		}

		private PageFactory _initConfigPageWithBlink() {
			List<string> blinkOptions = new List<string> {"Yes", "No"};
			List<string> blinkOptionsKeys = new List<string> {"1", "0"};

			return _initConfigPageWithColor()
				.WithDropDownSelectList(InputIdLedBlink, "Blink", blinkOptions, blinkOptionsKeys, _getFieldSelection(InputIdLedBlink));
		}
	}

	internal interface IGetDevicesActionListener {
		Dictionary<string, WX200Device> GetDevices();

		DeviceCollection GetDeviceCollection(string filter);
		
		IEnumerable<string> GetDeviceGroups();
		void WriteLog(ELogType logType, string message, [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string caller = null);
	}
}
