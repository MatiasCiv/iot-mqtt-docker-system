using Microsoft.EntityFrameworkCore;
using WebApplication1.Models;

namespace WebApplication1.Data;

public class AppDbContext : DbContext
{
    public DbSet<Reading> Readings => Set<Reading>();
    public DbSet<Cultivo> Cultivos => Set<Cultivo>();
    public DbSet<Etapa> Etapas => Set<Etapa>();
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }
}