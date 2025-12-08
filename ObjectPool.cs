namespace PhotoOrganizer
{
    public static class ObjectPool<T> where T : new()
    {
        static readonly List<T> pool = new();

        public static T Get()
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
            return new T();
        }

        public static void Return(T obj)
        {
            lock (pool)
            {
                pool.Add(obj);
            }
        }
    }
}
