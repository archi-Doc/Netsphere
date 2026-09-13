using Arc.Collections;

namespace NetsphereTest;

[NetService]
public interface ITestService : INetService
{
    public Task Send(int x);
}

[NetService]
public interface ITestService2 : INetService
{
    public Task Send2(int x);
}

[NetService]
public interface ITestService3 : INetService
{
    public Task Send3(string x, int y);

    public Task<int> Increment3(int x);

    public Task<BytePool.RentedMemory> SendMemoryOwner(BytePool.RentedMemory rentMemory);

    public Task<BytePool.RentedReadOnlyMemory> SendReadOnlyMemoryOwner(BytePool.RentedReadOnlyMemory rentMemory);
}

[NetObject]
public class TestServiceImpl0 : ITestService2
{
    public void Test()
    {
    }

    public async Task Send2(int x)
    {
    }
}

// [NetObject]
public class TestServiceImpl : ITestService
{
    public async Task Send(int x)
    {
        return;
    }
}

[NetObject]
public class TestServiceImpl2 : TestServiceImpl
{
    public async Task Send2(int x)
    {
        return;
    }
}

public class ParentClass
{
    [NetObject]
    internal class NestedServiceImpl3 : ITestService3
    {
        public async Task<int> Increment3(int x)
        {
            Console.WriteLine("Increment3");
            return x + 1;
        }

        public async Task Send3(string x, int y)
        {
        }

        public async Task<BytePool.RentedMemory> SendMemoryOwner(BytePool.RentedMemory rentMemory)
        {
            return rentMemory;
        }

        public async Task<BytePool.RentedReadOnlyMemory> SendReadOnlyMemoryOwner(BytePool.RentedReadOnlyMemory rentMemory)
        {
            return rentMemory;
        }
    }

    [NetService]
    public interface INestedService : INetService
    {
        public Task<int> Increment3(int x);
    }
}
