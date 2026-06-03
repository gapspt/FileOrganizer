using System.Diagnostics;

namespace PhotoOrganizer
{
    public class ObjectPool<T> where T : class
    {
        readonly List<T> pool = new();
        readonly Func<T> constructor;

        public ObjectPool(Func<T> constructor)
        {
            this.constructor = constructor;
        }

        public T Get()
        {
            lock (pool)
            {
                int count = pool.Count;
                if (count > 0)
                {
                    var result = pool[--count];
                    pool.RemoveAt(count);
                    return result;
                }
            }
            return constructor();
        }

        public void Return(T obj)
        {
            Debug.Assert(obj != null);
            lock (pool)
            {
                pool.Add(obj);
            }
        }

        public void ReturnIfNotNull(T? obj)
        {
            if (obj != null)
            {
                Return(obj);
            }
        }
    }

    public static class SimpleObjectPool<T> where T : class, new()
    {
        static readonly ObjectPool<T> pool = new(() => new T());

        public static T Get() => pool.Get();
        public static void Return(T obj) => pool.Return(obj);
        public static void ReturnIfNotNull(T? obj) => pool.ReturnIfNotNull(obj);
    }
}
