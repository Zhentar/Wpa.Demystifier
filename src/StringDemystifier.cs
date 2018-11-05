using System;
using System.Text;
using System.Threading;
using Microsoft.Performance.PerfCore4;
using Microsoft.Performance.PerfCore4.Symbols;

namespace Wpa.Demystifier
{
	public static class StringDemystifier
	{
		private static readonly ThreadLocal<StringBuilder> s_builder = new ThreadLocal<StringBuilder>(() => new StringBuilder());

		private static readonly UnmanagedString rootString = UnmanagedString.CreateWillNeverFree("[Root]");

		public static string SymbolToString(SymbolData symbol, uint symbolLoadFailure)
		{
			switch (symbol.BaseAddress.ToBytes)
			{
				case 0:
					if (symbol.SymbolName.EqualsCaseInsensitiveInvariant(rootString))
					{
						return "[Root]";
					}
					return "?!?";
				case 1:
					return "";
				case 2:
					return "n/a";
				case 3:
					return "[Idle]";
			}

			var isJittedSymbol = symbol.ProcessImageData.UsageType == 4;

			var sb = s_builder.Value;
			sb.Clear();
			UnmanagedString moduleName = symbol.ProcessImageData.ImageName.FileName;
			if (!moduleName.HasValue) { moduleName = symbol.SymbolImageData.OriginalFileName; }

			var moduleSpan = moduleName.AsSpan();
			if (isJittedSymbol && (moduleSpan.EndsWith(".dll".AsSpan(), StringComparison.OrdinalIgnoreCase) || moduleSpan.EndsWith(".exe".AsSpan(), StringComparison.OrdinalIgnoreCase)))
			{
				moduleSpan = moduleSpan.Slice(0, moduleSpan.Length - 4);
			}

			sb.Append(moduleSpan);
			if (moduleSpan.IsEmpty) { sb.Append('?'); }
			sb.Append('!');
			if (symbolLoadFailure > 0)
			{
				sb.Append('<');
				DecodedSymbol.SymbolLoadFailureDictionary.TryGetValue(symbolLoadFailure, out var failure);
				sb.Append(failure ?? "Unknown failure");
				sb.Append('>');
				return sb.ToString();
			}

			var functionName = symbol.SymbolName.AsSpan();
			if (functionName.IsEmpty)
			{
				sb.Append('?');
			}
			else
			{
				if (isJittedSymbol)
				{
					if (functionName.StartsWith(moduleSpan))
					{
						functionName = functionName.Slice(moduleSpan.Length);
						functionName = functionName.TrimStart('.');
					}
					if (functionName.EndsWith(" 0x0".AsSpan()))
					{
						functionName = functionName.Slice(0, functionName.Length - 4);
					}
					ProcessString(functionName, sb);
				}
				else
				{	//TODO: ngen image cleanup
					sb.Append(functionName);
				}
			}
			return sb.ToString();
		}

		public static void ProcessString(ReadOnlySpan<char> input, StringBuilder sb)
		{
			var classPiece = input.SplitOnce("::".AsSpan(), out var methodPiece);
			if (!methodPiece.IsEmpty)
			{
				classPiece = classPiece.SplitOnce('[', out var typeParamList);
				typeParamList = typeParamList.TrimEnd(']');
				//var actualClassName = classPiece.SplitOnce("+<".AsSpan(), out var genClassName);

				var trailingJunk = ReadOnlySpan<char>.Empty;

				if (methodPiece.StartsWith("<".AsSpan()))
				{
					methodPiece = methodPiece.Slice(1);
					methodPiece = methodPiece.SplitOnce('>', out trailingJunk);
				}


				RecursiveParseClassName(sb, classPiece, ref typeParamList, methodPiece);

				if (trailingJunk.StartsWith(LambdaMethodOrdinalPrefix.AsSpan()))
				{
					trailingJunk = trailingJunk.Slice(LambdaMethodOrdinalPrefix.Length);
					int ordinal = trailingJunk[0] - '0';
					if (trailingJunk.Length > 1)
					{
						int.TryParse(new string(trailingJunk.ToArray()), out ordinal);
					}

					if (ordinal > 0)
					{
						sb.Append(" [" + ordinal + "]");
					}
				}
				else if (trailingJunk.StartsWith(LocalFunctionPrefix.AsSpan()))
				{
					AppendLocalFunctionName(sb, trailingJunk);
				}
				else
				{
					sb.Append(trailingJunk);
				}
			}
			else
			{
				sb.Append(input);
			}
		}

		private static void RecursiveParseClassName(StringBuilder sb, ReadOnlySpan<char> remainingTypeName, ref ReadOnlySpan<char> typeParamList, ReadOnlySpan<char> methodPiece)
		{
			var classNameAndTypeParamCount = remainingTypeName.SplitOnce('+', out remainingTypeName);
			var className = classNameAndTypeParamCount.SplitOnce('`', out var thisClassTypeParamCount);
			StringBuilder savedLambdaTypes = null;

			bool isLambdaClosure = false;
			if (className.StartsWith(LambaDisplayClassPrefix.AsSpan()))
			{
				isLambdaClosure = true;
				sb.Length--; //Trim off the trailing +
				if (!thisClassTypeParamCount.IsEmpty && !remainingTypeName.IsEmpty)
				{
					savedLambdaTypes = new StringBuilder();
					AppendTypeParams(savedLambdaTypes, ref typeParamList, thisClassTypeParamCount);
					thisClassTypeParamCount = ReadOnlySpan<char>.Empty;
				}
			}
			else
			{
				if (className.StartsWith("<".AsSpan()))
				{  //Either a state machine ("<parent>d__") or a local function ("<parent>g__")
				   //Nesting is possible
					className = className.TrimStart('<');
					var ownerName = className.SplitOnce('>', out className);
					sb.Append(ownerName);
					//Can't tell whether the type params are for the parent, the local function, or both.
					//The parent seems like the safest guess.
					AppendTypeParams(sb, ref typeParamList, thisClassTypeParamCount); 
					if (className.StartsWith(LocalFunctionPrefix.AsSpan()))
					{
						AppendLocalFunctionName(sb, className);
					}

				}
				else
				{
					sb.Append(className);
					AppendTypeParams(sb, ref typeParamList, thisClassTypeParamCount);
				}
			}


			if (!remainingTypeName.IsEmpty)
			{
				sb.Append('+');
				RecursiveParseClassName(sb, remainingTypeName, ref typeParamList, methodPiece);
			}
			else
			{
				sb.Append("::");
				sb.Append(methodPiece);
			}

			if (isLambdaClosure)
			{
				AppendTypeParams(sb, ref typeParamList, thisClassTypeParamCount);
				if (savedLambdaTypes != null)
				{
					sb.Append(savedLambdaTypes);
				}
				sb.Append("+()=>{}");
			}
		}

		private static void AppendLocalFunctionName(StringBuilder sb, ReadOnlySpan<char> generatedName)
		{
			generatedName = generatedName.Slice(3); //chop off g__
			generatedName = generatedName.SplitOnce('|', out _);
			sb.Append('+');
			sb.Append(generatedName);
		}


		private static void AppendTypeParams(StringBuilder sb, ref ReadOnlySpan<char> typeParamList, ReadOnlySpan<char> typeParamCountSpan)
		{
			if (!typeParamCountSpan.IsEmpty)
			{
				int count = typeParamCountSpan[0] - '0';
				if (count > 0 && count < 10) //If you've got more than 10 type params the name is going to be truncated & un-parseable anyway
				{
					int tCount = 0;
					sb.Append('<');
					for (int i = 0; i < count; i++)
					{
						if (i != 0)
						{
							sb.Append(',');
						}

						sb.Append(PopTypeParam(ref typeParamList, ref tCount));
					}

					sb.Append('>');
				}
			}
		}

		private static ReadOnlySpan<char> PopTypeParam(ref ReadOnlySpan<char> typeParamList, ref int TCount)
		{
			var nextType = typeParamList.SplitOnce(',', out typeParamList);
			if (nextType.SequenceEqual(System__Canon.AsSpan()))
			{
				TCount++;
				if (TCount > 1)
				{
					return ("T" + TCount).AsSpan();
				}

				return "T".AsSpan();
			}
			//The full namespace tends to be a rather bit excessive... chop it off.
			return nextType.Slice(nextType.LastIndexOf('.') + 1);
		}

		private const string System__Canon = "System.__Canon";
		private static readonly string LambdaMethodOrdinalPrefix = (char)GeneratedNameKind.LambdaMethod + "__";
		private static readonly string LambaDisplayClassPrefix = "<>" + (char) GeneratedNameKind.LambdaDisplayClass + "__";
		private static readonly string LocalFunctionPrefix = (char) GeneratedNameKind.LocalFunction + "__";

		//From:
		//https://github.com/dotnet/roslyn/blob/master/src/Compilers/CSharp/Portable/Symbols/Synthesized/GeneratedNameKind.cs
		internal enum GeneratedNameKind
		{
			None = 0,

			// Used by EE:
			ThisProxyField = '4',
			HoistedLocalField = '5',
			DisplayClassLocalOrField = '8',
			LambdaMethod = 'b',
			LambdaDisplayClass = 'c',
			StateMachineType = 'd',
			LocalFunction = 'g', // note collision with Deprecated_InitializerLocal, however this one is only used for method names

			// Used by EnC:
			AwaiterField = 'u',
			HoistedSynthesizedLocalField = 's',

			// Currently not parsed:
			StateMachineStateField = '1',
			IteratorCurrentBackingField = '2',
			StateMachineParameterProxyField = '3',
			ReusableHoistedLocalField = '7',
			LambdaCacheField = '9',
			FixedBufferField = 'e',
			AnonymousType = 'f',
			TransparentIdentifier = 'h',
			AnonymousTypeField = 'i',
			AutoPropertyBackingField = 'k',
			IteratorCurrentThreadIdField = 'l',
			IteratorFinallyMethod = 'm',
			BaseMethodWrapper = 'n',
			AsyncBuilderField = 't',
			DynamicCallSiteContainerType = 'o',
			DynamicCallSiteField = 'p',
			AsyncIteratorPromiseOfValueOrEndBackingField = 'v', // last

			// Deprecated - emitted by Dev12, but not by Roslyn.
			// Don't reuse the values because the debugger might encounter them when consuming old binaries.
			[Obsolete]
			Deprecated_OuterscopeLocals = '6',
			[Obsolete]
			Deprecated_IteratorInstance = 'a',
			[Obsolete]
			Deprecated_InitializerLocal = 'g',
			[Obsolete]
			Deprecated_AnonymousTypeTypeParameter = 'j',
			[Obsolete]
			Deprecated_DynamicDelegate = 'q',
			[Obsolete]
			Deprecated_ComrefCallLocal = 'r',
		}

	}
}
