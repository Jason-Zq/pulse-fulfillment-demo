using Microsoft.EntityFrameworkCore;
using Pulse.Domain;
using Pulse.Infrastructure.Persistence;

namespace Pulse.Infrastructure.Orders;

/// <summary>
/// OrderNo 为 M6b v0.2 新增的末尾可选参数，保持 M4 的 6 参数构造调用不变。
/// </summary>
public sealed record OrderListQuery(
    string? Status,
    string? Sku,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int Page,
    int PageSize,
    string? OrderNo = null);

public sealed record OrderListItem(
    long Id,
    string OrderNo,
    string Status,
    decimal TotalAmount,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    string Sku);

public sealed record OrderListResult(
    IReadOnlyList<OrderListItem> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>
/// M4 4.2：订单列表查询（DTO 契约 GET /api/orders）。sku 筛选走 EXISTS（Any），
/// 分页基于 orders 本身，单 SKU 多明细不会重复计；按 CreatedAtUtc 倒序。
/// M6b 增加 orderNo 模糊匹配（Contains → LIKE），供「我的订单」搜索。
/// 只做即时查询，不落任何汇总表。
/// </summary>
public sealed class OrderQueryService
{
    private const int DefaultPageSize = 20;

    private readonly PulseDbContext _db;

    public OrderQueryService(PulseDbContext db) => _db = db;

    public async Task<OrderListResult> ListAsync(
        OrderListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? DefaultPageSize : query.PageSize;

        var orders = _db.Orders.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Status)
            && Enum.TryParse<OrderStatus>(query.Status, ignoreCase: true, out var status))
        {
            orders = orders.Where(o => o.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Sku))
        {
            var sku = query.Sku;
            orders = orders.Where(o => o.Items.Any(i => i.Sku == sku));
        }

        if (!string.IsNullOrWhiteSpace(query.OrderNo))
        {
            var orderNo = query.OrderNo;
            orders = orders.Where(o => o.OrderNo.Contains(orderNo));
        }

        if (query.FromUtc is { } fromUtc)
        {
            orders = orders.Where(o => o.CreatedAtUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            orders = orders.Where(o => o.CreatedAtUtc <= toUtc);
        }

        var total = await orders.CountAsync(cancellationToken);

        // enum -> string 放在内存做，避免依赖 provider 对 Enum.ToString() 的翻译。
        var rows = await orders
            .OrderByDescending(o => o.CreatedAtUtc)
            .ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.Id,
                o.OrderNo,
                o.Status,
                o.TotalAmount,
                o.CreatedAtUtc,
                o.ExpiresAtUtc,
                Sku = o.Items.Select(i => i.Sku).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new OrderListItem(
            r.Id,
            r.OrderNo,
            r.Status.ToString(),
            r.TotalAmount,
            r.CreatedAtUtc,
            r.ExpiresAtUtc,
            r.Sku ?? string.Empty)).ToList();

        return new OrderListResult(items, total, page, pageSize);
    }
}
