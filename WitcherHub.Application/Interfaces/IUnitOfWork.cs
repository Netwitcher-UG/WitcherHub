using System;
using System.Collections.Generic;
using System.Text;
using WitcherHub.Domain.Commen;

namespace WitcherHub.Application.Interfaces
{

    public interface IUnitOfWork : IAsyncDisposable
    {
        IRepository<TEntity> Repo<TEntity>()
            where TEntity : BaseEntity;

        Task<int> SaveChangesAsync(CancellationToken ct = default);

        Task BeginTransactionAsync(CancellationToken ct = default);
        Task CommitTransactionAsync(CancellationToken ct = default);
        Task RollbackTransactionAsync(CancellationToken ct = default);
    }
}
