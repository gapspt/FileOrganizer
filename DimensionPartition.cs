namespace PhotoOrganizer
{
    public class DimensionPartition<T>
    {
        List<T>? values;
        DimensionPartition<T>? singleChild;
        int singleChildCoordinate;
        Dictionary<int, DimensionPartition<T>>? children;

        public IReadOnlyList<T>? Values => values;

        public void Add(T item)
        {
            (values ??= new()).Add(item);
        }

        public DimensionPartition<T>? GetDimensionCoordinate(int coordinate)
        {
            if (singleChild != null && singleChildCoordinate == coordinate)
            {
                return singleChild;
            }

            if (children != null)
            {
                children.TryGetValue(coordinate, out var result);
                return result;
            }

            return null;
        }

        public DimensionPartition<T> GetOrAddDimensionCoordinate(int coordinate)
        {
            DimensionPartition<T>? result;

            if (singleChild != null)
            {
                if (singleChildCoordinate == coordinate)
                {
                    return singleChild;
                }

                children = new();
                children[singleChildCoordinate] = singleChild;
                children[coordinate] = result = new();
                singleChild = null;
                return result;
            }

            if (children != null)
            {
                if (!children.TryGetValue(coordinate, out result))
                {
                    children[coordinate] = result = new();
                }
                return result;
            }

            singleChild = new();
            singleChildCoordinate = coordinate;
            return singleChild;
        }
    }
}
