using System;
using System.Collections.Generic;
using System.Text;

namespace WitcherHub.Infrastructure.Data.Models
{
    public class BaseEntity
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
