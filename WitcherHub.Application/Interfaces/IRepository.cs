

using WitcherHub.Domain.Commen;
using System.Linq.Expressions;

namespace WitcherHub.Application.Interfaces
{

    public interface IRepository<TEntity>
        where TEntity : BaseEntity
    {
        Task<TEntity?> GetByIdAsync(
            Guid id,
            CancellationToken ct = default,
            bool asNoTracking = true,
            params Expression<Func<TEntity, object>>[] includes);

        Task<TEntity?> FirstOrDefaultAsync(
            Expression<Func<TEntity, bool>> predicate,
            CancellationToken ct = default,
            bool asNoTracking = true,
            params Expression<Func<TEntity, object>>[] includes);

        Task<IReadOnlyList<TEntity>> ListAsync(
            Expression<Func<TEntity, bool>>? predicate = null,
            CancellationToken ct = default,
            bool asNoTracking = true,
            params Expression<Func<TEntity, object>>[] includes);

        Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default);
        Task<long> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken ct = default);

        Task AddAsync(TEntity entity, CancellationToken ct = default);
        Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);

        void Update(TEntity entity);
        void Remove(TEntity entity);
        void RemoveRange(IEnumerable<TEntity> entities);
    }

}
