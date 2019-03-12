using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.Performance.AddIns;
using Microsoft.Performance.DataEngine;
using Microsoft.Performance.PerfCore4;
using Microsoft.Performance.PerfCore4.Symbols;
using Microsoft.Performance.Shell.AddIns;

namespace Wpa.Demystifier
{
	public interface IDemystifyService : IWpaService
	{ }

	[WpaServiceExport(typeof(IDemystifyService))]
	public class DemystifyAddin : UISessionComponent, IDemystifyService
	{
		static DemystifyAddin()
		{
			try
			{
				Utils.WriteDetour<CellValue, FakeCellValue>(nameof(FakeCellValue.FormatValue));
			}
			catch (Exception exc)
			{
				MessageBox.Show($"{exc.Message}\n{exc.StackTrace}");
			}
		}


		private class FakeCellValue
		{
			public static string FormatValue(string format, IFormatProvider formatProvider, object value)
			{
				string text = FormatUsingCustomFormatter(format, formatProvider, null,  value);
				if (text == null)
				{
					if (value is IFormattable formattable)
					{
						text = formattable.ToString(format, formatProvider);
					}
					else if (value is DecodedSymbol symbol)
					{
						ref var exposed = ref Unsafe.As<DecodedSymbol, FakeDecodedSymbol>(ref symbol);
						text = StringDemystifier.SymbolToString(exposed.symbolData, exposed.symbolLoadFailure);
					}
					else if (value != null)
					{
						text = value.ToString();
					}
				}

				return text ?? string.Empty;
			}

			[StructLayout(LayoutKind.Sequential)]
			private readonly struct FakeDecodedSymbol
			{
				public readonly SymbolData symbolData;
				public readonly uint symbolLoadFailure;
				private readonly bool pgoPhaseValid;
			}

			private static readonly Func<string, IFormatProvider, DataContext, object, string> FormatUsingCustomFormatter =
				Utils.GetStaticMethodInvoker<string, IFormatProvider, DataContext, object, string>(typeof(CellValue), "FormatUsingCustomFormatter");
		}

	}

}
