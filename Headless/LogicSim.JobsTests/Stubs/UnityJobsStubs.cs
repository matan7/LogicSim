// Minimal functional stand-ins for the Unity APIs used by LogicSim.Simulation.Jobs.
// Signatures mirror UnityEngine.CoreModule; jobs run synchronously when scheduled.
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Unity.Collections
{
    public enum Allocator { Invalid = 0, None = 1, Temp = 2, TempJob = 3, Persistent = 4 }
    public enum NativeArrayOptions { UninitializedMemory = 0, ClearMemory = 1 }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter)]
    public sealed class ReadOnlyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class NativeDisableParallelForRestrictionAttribute : Attribute { }

    public unsafe struct NativeArray<T> : IDisposable where T : struct
    {
        internal void* m_Buffer;
        internal int m_Length;

        public NativeArray(int length, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            long bytes = (long)Math.Max(1, length) * Unsafe.SizeOf<T>();
            m_Buffer = (void*)Marshal.AllocHGlobal((IntPtr)bytes);
            new Span<byte>(m_Buffer, (int)bytes).Clear();
            m_Length = length;
            Tracker.Allocated++;
        }

        public NativeArray(T[] array, Allocator allocator) : this(array.Length, allocator)
        {
            for (int i = 0; i < array.Length; i++) this[i] = array[i];
        }

        public int Length => m_Length;
        public bool IsCreated => m_Buffer != null;

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)m_Length) throw new IndexOutOfRangeException($"{index} / {m_Length}");
                return Unsafe.Read<T>((byte*)m_Buffer + (long)index * Unsafe.SizeOf<T>());
            }
            set
            {
                if ((uint)index >= (uint)m_Length) throw new IndexOutOfRangeException($"{index} / {m_Length}");
                Unsafe.Write((byte*)m_Buffer + (long)index * Unsafe.SizeOf<T>(), value);
            }
        }

        public static void Copy(NativeArray<T> src, NativeArray<T> dst)
        {
            if (src.Length != dst.Length) throw new ArgumentException("Length mismatch");
            Buffer.MemoryCopy(src.m_Buffer, dst.m_Buffer, (long)dst.Length * Unsafe.SizeOf<T>(), (long)src.Length * Unsafe.SizeOf<T>());
        }

        public void Dispose()
        {
            if (m_Buffer == null) throw new ObjectDisposedException("NativeArray");
            Marshal.FreeHGlobal((IntPtr)m_Buffer);
            m_Buffer = null;
            Tracker.Allocated--;
        }
    }

    public static class Tracker
    {
        public static int Allocated;
    }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    public static unsafe class NativeArrayUnsafeUtility
    {
        public static void* GetUnsafePtr<T>(this NativeArray<T> nativeArray) where T : struct => nativeArray.m_Buffer;
        public static void* GetUnsafeReadOnlyPtr<T>(this NativeArray<T> nativeArray) where T : struct => nativeArray.m_Buffer;
    }

    public static unsafe class UnsafeUtility
    {
        public static void MemClear(void* destination, long size) => new Span<byte>(destination, checked((int)size)).Clear();
        public static int SizeOf<T>() where T : struct => System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
    }
}

namespace Unity.Jobs
{
    public interface IJob { void Execute(); }
    public interface IJobParallelFor { void Execute(int index); }

    public struct JobHandle
    {
        public static int Scheduled;
        public void Complete() { }
    }

    public static class IJobExtensions
    {
        public static JobHandle Schedule<T>(this T jobData, JobHandle dependsOn = default) where T : struct, IJob
        {
            JobHandle.Scheduled++;
            jobData.Execute();
            return default;
        }
    }

    public static class IJobParallelForExtensions
    {
        public static JobHandle Schedule<T>(this T jobData, int arrayLength, int innerloopBatchCount, JobHandle dependsOn = default)
            where T : struct, IJobParallelFor
        {
            JobHandle.Scheduled++;
            // Run items in reverse to catch accidental order dependencies between work items.
            for (int i = arrayLength - 1; i >= 0; i--)
                jobData.Execute(i);
            return default;
        }
    }
}
