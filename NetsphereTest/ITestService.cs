using Arc.Collections;

namespace NetsphereTest;

[NetServiceInterface]
public interface ITestService : INetService
{
    public NetTask Send(int x);
}

[NetServiceInterface]
public interface ITestService2 : INetService
{
    public NetTask Send2(int x);
}

[NetServiceInterface]
public interface ITestService3 : INetService
{
    public NetTask Send3(string x, int y);

    public NetTask<int> Increment3(int x);

    public NetTask<BytePool.RentMemory> SendMemoryOwner(BytePool.RentMemory rentMemory);

    public NetTask<BytePool.RentReadOnlyMemory> SendReadOnlyMemoryOwner(BytePool.RentReadOnlyMemory rentMemory);
}

[NetServiceObject]
public class TestServiceImpl0 : ITestService2
{
    public void Test()
    {
    }

    public async NetTask Send2(int x)
    {
    }
}

// [NetServiceObject]
public class TestServiceImpl : ITestService
{
    public async NetTask Send(int x)
    {
        return;
    }
}

[NetServiceObject]
public class TestServiceImpl2 : TestServiceImpl
{
    public async NetTask Send2(int x)
    {
        return;
    }
}

public class ParentClass
{
    [NetServiceObject]
    internal class NestedServiceImpl3 : ITestService3
    {
        public async NetTask<int> Increment3(int x)
        {
            Console.WriteLine("Increment3");
            return x + 1;
        }

        public async NetTask Send3(string x, int y)
        {
        }

        public async NetTask<BytePool.RentMemory> SendMemoryOwner(BytePool.RentMemory rentMemory)
        {
            return rentMemory;
        }

        public async NetTask<BytePool.RentReadOnlyMemory> SendReadOnlyMemoryOwner(BytePool.RentReadOnlyMemory rentMemory)
        {
            return rentMemory;
        }
    }

    [NetServiceInterface]
    public interface INestedService : INetService
    {
        public NetTask<int> Increment3(int x);
    }
}
