﻿using System;
using System.Drawing;
using ReClassNET.Memory;
using ReClassNET.UI;

namespace ReClassNET.Nodes
{
	public class Hex64Node : BaseHexCommentNode
	{
		public override int MemorySize => 8;

		public override void GetUserInterfaceInfo(out string name, out Image icon)
		{
			name = "Hex64";
			icon = Properties.Resources.B16x16_Button_Hex_64;
		}

		public override bool UseMemoryPreviewToolTip(HotSpot spot, MemoryBuffer memory, out IntPtr address)
		{
			var value = memory.ReadObject<UInt64FloatDoubleData>(Offset);

			address = value.IntPtr;

			return memory.Process.GetSectionToPointer(value.IntPtr) != null;
		}

		public override string GetToolTipText(HotSpot spot, MemoryBuffer memory)
		{
			var value = memory.ReadObject<UInt64FloatDoubleData>(Offset);

			return $"Int64: {value.LongValue}\nUInt64: 0x{value.ULongValue:X016}\nFloat: {value.FloatValue:0.000}\nDouble: {value.DoubleValue:0.000}";
		}

		public override Size Draw(ViewInfo view, int x, int y)
		{
			return Draw(view, x, y, view.Settings.ShowNodeText ? view.Memory.ReadPrintableAsciiString(Offset, 8) + " " : null, 8);
		}

		public override void Update(HotSpot spot)
		{
			Update(spot, 8);
		}

		protected override int AddComment(ViewInfo view, int x, int y)
		{
			x = base.AddComment(view, x, y);

			var value = view.Memory.ReadObject<UInt64FloatDoubleData>(Offset);

			x = AddComment(view, x, y, value.FloatValue, value.IntPtr, value.UIntPtr);

			return x;
		}
	}
}
