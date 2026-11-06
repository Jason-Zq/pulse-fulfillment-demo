using Xunit;

// API 集成测试共享同一个本地 SQLite 演示库，禁用并行避免文件锁竞争。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
