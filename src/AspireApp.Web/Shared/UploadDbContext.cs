using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using System.Collections.Generic;

namespace AspireApp.Web.Data
{
	public class UploadDbContext : DbContext
    {
        public UploadDbContext(DbContextOptions<UploadDbContext> options) : base(options) { }
        public DbSet<FileMetadata> Files => Set<FileMetadata>();
    }

}
