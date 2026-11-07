namespace Pulse.Infrastructure.Persistence;

/// <summary>
/// 演示商品目录（M7）：API 启动种子（Program.cs）与实验台 /api/lab/reset（LabService）
/// 共用这一份定义，避免两处各自维护导致漂移。
///
/// 背景：M7 增加 Product.Category 并把商品扩到 8 个 SKU 时只改了启动种子，
/// reset 仍回填 2 个无分类 SKU，一按「重置演示数据」商城就会退回旧状态。
/// </summary>
public static class DemoCatalog
{
    public sealed record Seed(
        string Sku,
        string Name,
        decimal Price,
        string Category,
        int Available);

    public static readonly IReadOnlyList<Seed> Products =
    [
        new("SKU-1", "演示商品 A", 99.5m, "数码电器", 100),
        new("SKU-2", "演示商品 B", 199m, "家居生活", 50),
        new("SKU-3", "无线降噪耳机", 299m, "数码电器", 30),
        new("SKU-4", "北欧风落地灯", 159m, "家居生活", 40),
        new("SKU-5", "纯棉基础 T 恤", 79m, "服饰鞋包", 80),
        new("SKU-6", "保湿修护精华", 239m, "美妆个护", 25),
        new("SKU-7", "有机燕麦片", 49m, "食品生鲜", 60),
        new("SKU-8", "机械键盘", 359m, "数码电器", 20),
    ];
}
