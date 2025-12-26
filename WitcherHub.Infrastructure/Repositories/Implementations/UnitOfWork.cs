using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using WitcherHub.Application.Interfaces;
using WitcherHub.Domain.Commen;
using WitcherHub.Infrastructure.Data.Context;

namespace WitcherHub.Infrastructure.Repositories.Implementations
{

    public class UnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _ctx;
        private IDbContextTransaction? _tx;

        private readonly ConcurrentDictionary<Type, object> _repos = new();

        public UnitOfWork(AppDbContext ctx)
        {
            _ctx = ctx;
        }

        public IRepository<TEntity> Repo<TEntity>()
            where TEntity : BaseEntity
        {
            var type = typeof(TEntity);

            var repo = _repos.GetOrAdd(type, _ =>
                new EfRepository<TEntity>(_ctx));

            return (IRepository<TEntity>)repo;
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
            => _ctx.SaveChangesAsync(ct);

        public async Task BeginTransactionAsync(CancellationToken ct = default)
        {
            if (_tx != null) return;
            _tx = await _ctx.Database.BeginTransactionAsync(ct);
        }

        public async Task CommitTransactionAsync(CancellationToken ct = default)
        {
            if (_tx == null) return;

            await _ctx.SaveChangesAsync(ct);
            await _tx.CommitAsync(ct);
            await _tx.DisposeAsync();
            _tx = null;
        }

        public async Task RollbackTransactionAsync(CancellationToken ct = default)
        {
            if (_tx == null) return;

            await _tx.RollbackAsync(ct);
            await _tx.DisposeAsync();
            _tx = null;
        }

        public async ValueTask DisposeAsync()
        {
            if (_tx != null)
            {
                await _tx.DisposeAsync();
                _tx = null;
            }

            await _ctx.DisposeAsync();
        }
    }
}
