namespace PhotoOrganizer
{
    public class ObjectPool<T>
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
            lock (pool)
            {
                pool.Add(obj);
            }
        }
    }

    public static class SimpleObjectPool<T> where T : new()
    {
        static readonly ObjectPool<T> pool = new(() => new T());

        public static T Get() => pool.Get();
        public static void Return(T obj) => pool.Return(obj);
    }
}
