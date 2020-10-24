using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HSPI_WX200MultiStatus.Enums;

namespace HSPI_WX200MultiStatus.DataStructures {
	public class DeviceCollection : IEnumerable<WX200Device> {
		public int Count => _list.Count;
		private readonly List<WX200Device> _list;

		public DeviceCollection(List<WX200Device> list) {
			_list = new List<WX200Device>(list);
		}

		public DeviceCollection(WX200Device[] list) {
			_list = new List<WX200Device>(list);
		}

		public DeviceCollection(WX200Device device) {
			_list = new List<WX200Device> {device};
		}

		public byte GetMaxLedCount() {
			byte max = 1;
			foreach (WX200Device device in _list) {
				max = Math.Max(max, device.GetLedCount());
			}

			return max;
		}

		public bool SupportsLed(byte ledIndex) {
			return ledIndex < GetMaxLedCount();
		}

		public bool ContainsDevice(int deviceRef) {
			return _list.Exists(device => device.DevRef == deviceRef);
		}

		public int GetNumDevicesNotSupportingLed(byte ledIndex) {
			if (ledIndex == 255) {
				// All LEDs. Naturally all devices support illuminating all their LEDs.
				return 0;
			}
			
			return _list.Count(device => ledIndex >= device.GetLedCount());
		}

		public void SetStatusLed(byte ledIndex, WX200StatusModeColor color, bool blink) {
			foreach (WX200Device device in _list) {
				if (ledIndex == 255) {
					for (byte i = 0; i < device.GetLedCount(); i++) {
						device.SetStatusLed(i, color, blink);
					}
				} else {
					device.SetStatusLed(ledIndex, color, blink);
				}
			}
		}
		
		public IEnumerator<WX200Device> GetEnumerator() {
			return _list.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator() {
			return GetEnumerator();
		}
	}
}
