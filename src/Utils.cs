using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Performance.PerfCore4;

namespace Wpa.Demystifier
{
	static class Utils
	{
		public const BindingFlags UniversalBindingFlags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		public static void Append(this StringBuilder @this, ReadOnlySpan<char> chars)
		{
			for (int i = 0; i < chars.Length; i++)
			{
				@this.Append(chars[i]);
			}
		}

		public static Func<TArgs1, TArgs2, TArgs3, TArgs4, TResult> GetStaticMethodInvoker<TArgs1, TArgs2, TArgs3, TArgs4, TResult>(Type staticType, string methodName)
		{
			var methodInfo = staticType.GetMethod(methodName, UniversalBindingFlags, null, new[] { typeof(TArgs1), typeof(TArgs2), typeof(TArgs3), typeof(TArgs4) }, null);

			var argParams = new[]
				  { Expression.Parameter(typeof(TArgs1), "arg1"),
					Expression.Parameter(typeof(TArgs2), "arg2"),
					Expression.Parameter(typeof(TArgs3), "arg3"),
					Expression.Parameter(typeof(TArgs4), "arg4")};

			var call = Expression.Call(null, methodInfo, argParams);

			var lambda = Expression.Lambda(typeof(Func<TArgs1, TArgs2, TArgs3, TArgs4, TResult>), call, argParams[0], argParams[1], argParams[2], argParams[3]);
			var compiled = (Func<TArgs1, TArgs2, TArgs3, TArgs4, TResult>)lambda.Compile();
			return compiled;
		}

		private enum Protection { PAGE_EXECUTE_READWRITE = 0x40, }
		[DllImport("kernel32.dll")]
		private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, Protection flNewProtect, out Protection lpflOldProtect);


		public static unsafe void WriteDetour<TOriginal, TReplacement>(string methodName)
		{
			var replacementMethod = typeof(TReplacement).GetMethod(methodName, UniversalBindingFlags);
			var paramTypes = replacementMethod.GetParameters().Select(p => p.ParameterType).ToArray();
			var origMethod = typeof(TOriginal).GetMethod(methodName, UniversalBindingFlags, null, paramTypes, null);
			var originalFun = GetMethodPointer(origMethod);
			var destination = GetMethodPointer(replacementMethod).ToInt64();

			VirtualProtect(originalFun, new UIntPtr(1), Protection.PAGE_EXECUTE_READWRITE, out _);
			Write(Write(Write(originalFun.ToPointer(), movabs_rax), destination), jmp_rax);
		}

		private const ushort movabs_rax = 0xB848;
		private const ushort jmp_rax = 0xE0FF;

		private static IntPtr GetMethodPointer(MethodBase method)
		{
			RuntimeHelpers.PrepareMethod(method.MethodHandle); //Make sure it's JITted
			return method.MethodHandle.GetFunctionPointer();
		}

		private static unsafe void* Write<T>(void* address, T value)
		{
			Unsafe.Write(address, value);
			return Unsafe.Add<T>(address, 1);
		}

		public static unsafe ReadOnlySpan<char> AsSpan(this UnmanagedString @this)
		{
			char* ptr = @this.GetInternalPointer();
			return new ReadOnlySpan<char>(ptr, @this.Length);
		}

		public static ReadOnlySpan<T> SplitOnce<T>(this ReadOnlySpan<T> @this, ReadOnlySpan<T> delimiter, out ReadOnlySpan<T> tail) where T : IEquatable<T>
		{
			int delimIdx = @this.IndexOf(delimiter);
			if (delimIdx < 0)
			{
				tail = ReadOnlySpan<T>.Empty;
				return @this;
			}

			tail = @this.Slice(delimIdx + delimiter.Length);
			return @this.Slice(0, delimIdx);
		}

		public static ReadOnlySpan<T> SplitOnce<T>(this ReadOnlySpan<T> @this, T delimiter, out ReadOnlySpan<T> tail) where T : IEquatable<T>
		{
			int delimIdx = @this.IndexOf(delimiter);
			if (delimIdx < 0)
			{
				tail = ReadOnlySpan<T>.Empty;
				return @this;
			}

			tail = @this.Slice(delimIdx + 1);
			return @this.Slice(0, delimIdx);
		}

	}
}
