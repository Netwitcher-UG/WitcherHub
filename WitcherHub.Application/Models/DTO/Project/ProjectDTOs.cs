using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using static WitcherHub.Infrastructure.Data.Models.Enums;

namespace WitcherHub.Application.Models.DTO.Project
{
    public class CreateProjectDto
    {
        [Required]
        public Guid CustomerId { get; set; }

        [Required, MaxLength(250)]
        public string Title { get; set; } = "";

        public string? Description { get; set; }

        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
    }

    public class UpdateProjectDto
    {
        [MaxLength(250)]
        public string? Title { get; set; }
        public string? Description { get; set; }

        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
    }

    public class ChangeProjectStatusDto
    {
        public ProjectStatus Status { get; set; }
    }
}
