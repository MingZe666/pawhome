// 独立测试库按顺序运行，避免 MySql.Data 同步 TLS 握手的共享缓存并发竞态。
// 仅关闭 xUnit 用例之间的并行；上架/照片测试内部 Task.WhenAll 仍验证真实业务并发。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
