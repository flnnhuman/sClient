using System;
using System.Diagnostics;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Hosting;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
namespace sc
{
public static class RuntimeCompatibility {
	public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> kv, out TKey key, out TValue value) {
			key = kv.Key;
			value = kv.Value;
		}
}
}