using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using WitcherHub.Application.Interfaces;
using WitcherHub.Domain.Commen;
using WitcherHub.Infrastructure.Data.Context;

namespace WitcherHub.Infrastructure.Repositories.Implementations
{
    public class EfRepository<TEntity> : IRepository<TEntity>
           where TEntity : BaseEntity
    {
        protected readonly AppDbContext _db;
        protected readonly DbSet<TEntity> _set;

        public EfRepository(AppDbContext db)
        {
            _db = db;
            _set = db.Set<TEntity>();
        }

        public async Task<TEntity?> GetByIdAsync(
            Guid id,
            CancellationToken ct = default,
            bool asNoTracking = true,
            params Expression<Func<TEntity, object>>[] includes)
        {
            IQueryable<TEntity> q = _set;

            if (includes is { Length: > 0 })
                foreach (var inc in includes)
                    q = q.Include(inc);

            if (asNoTracking) q = q.AsNoTracking();

            return await q.FirstOrDefaultAsync(x => x.Id == id, ct);
        }

        public async Task<TEntity?> FirstOrDefaultAsync(
            Expression<Func<TEntity, bool>> predicate,
            CancellationToken ct = default,
            bool asNoTracking = true,
            params Expression<Func<TEntity, object>>[] includes)
        {
            IQueryable<TEntity> q = _set;

            if (includes is { Length: > 0 })
                foreach (var inc in includes)
                    q = q.Include(inc);

            if (asNoTracking) q = q.AsNoTracking();

            return await q.FirstOrDefaultAsync(predicate, ct);
        }
        public IQueryable<TEntity> Query(
    bool asNoTracking = true,
    params Expression<Func<TEntity, object>>[] includes)
        {
            IQueryable<TEntity> q = _set;

            if (includes is { Length: > 0 })
                foreach (var inc in includes)
                    q = q.Include(inc);

            if (asNoTracking) q = q.AsNoTracking();

            return q;
        }
        public IQueryable<TEntity> Query(
            Func<IQueryable<TEntity>, IQueryable<TEntity>> include,
            bool asNoTracking = true)
        {
            IQueryable<TEntity> q = _set;

            if (include is not null)
                q = include(q);

            if (asNoTracking)
                q = q.AsNoTracking();

            return q;
        }

        public async Task<IReadOnlyList<TEntity>> ListAsync(
            Expression<Func<TEntity, bool>>? predicate = null,
            CancellationToken ct = default,
            bool asNoTracking = true,
            params Expression<Func<TEntity, object>>[] includes)
        {
            IQueryable<TEntity> q = _set;

            if (predicate != null) q = q.Where(predicate);

            if (includes is { Length: > 0 })
                foreach (var inc in includes)
                    q = q.Include(inc);

            if (asNoTracking) q = q.AsNoTracking();

            return await q.ToListAsync(ct);
        }

        public Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
            => _set.AnyAsync(predicate, ct);

        public Task<long> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken ct = default)
            => predicate == null
                ? _set.LongCountAsync(ct)
                : _set.LongCountAsync(predicate, ct);

        public Task AddAsync(TEntity entity, CancellationToken ct = default)
            => _set.AddAsync(entity, ct).AsTask();

        public Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default)
            => _set.AddRangeAsync(entities, ct);

        public void Update(TEntity entity) => _set.Update(entity);
        public void Remove(TEntity entity) => _set.Remove(entity);
        public void RemoveRange(IEnumerable<TEntity> entities) => _set.RemoveRange(entities);
    }
}
