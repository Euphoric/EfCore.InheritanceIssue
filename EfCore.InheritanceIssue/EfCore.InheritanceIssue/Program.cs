// See https://aka.ms/new-console-template for more information

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace EfCore.InheritanceIssue;

public class ParentEntity
{
    public Guid Id { get; set; }
    public ChildBaseEntity? Child { get; set; }
}

public abstract class ChildBaseEntity
{
    public Guid Id { get; set; }
    public Guid ParentId { get; set; }
}

public class ChildEntity : ChildBaseEntity
{
    public String? ChildValue { get; set; }
}

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<ParentEntity> Parents { get; private set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var parent = modelBuilder.Entity<ParentEntity>();
        parent.HasOne(x => x.Child)
            .WithOne()
            .HasForeignKey<ChildBaseEntity>(x=>x.ParentId)
            ;
        var childBase = modelBuilder.Entity<ChildBaseEntity>();
        //childBase.UseTphMappingStrategy(); // Case 1 : Works correctly
        //childBase.UseTptMappingStrategy(); // Case 2 : Doesn't work
        childBase.UseTpcMappingStrategy(); // Case 3 : Doesn't work
        
        var child = modelBuilder.Entity<ChildEntity>();
        child.HasBaseType<ChildBaseEntity>();
    }
}

public class Program
{
    public static async Task Main(string[] args)
    {
        var services = await SetupPostgre();
        //var services = await SetupSqlServer();
        await ExecuteTest(services);
    }

    private static async Task<IServiceProvider> SetupPostgre()
    {
        var container = new PostgreSqlBuilder().Build();
        await container.StartAsync();
        var connectionString = container.GetConnectionString();
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(opt => { opt.UseNpgsql(connectionString); });
        services.AddLogging(builder => { builder.AddConsole(); });
        return services.BuildServiceProvider();
    }
    
    private static async Task<IServiceProvider> SetupSqlServer()
    {
        var container = new MsSqlBuilder().Build();
        await container.StartAsync();
        var connectionString = container.GetConnectionString();
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(opt => { opt.UseSqlServer(connectionString); });
        services.AddLogging(builder => { builder.AddConsole(); });
        return services.BuildServiceProvider();
    }

    private static async Task ExecuteTest(IServiceProvider services)
    {
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await context.Database.EnsureCreatedAsync();
        }

        var parentId = Guid.NewGuid();
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            context.Parents.Add(new ParentEntity {Id = parentId});
            await context.SaveChangesAsync();
        }

        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var parent = await context.Parents.FindAsync(parentId);
            var child = new ChildEntity {Id = Guid.NewGuid(), ParentId = parent!.Id, ChildValue = "test value"};
            
            //await context.Set<ChildBaseEntity>().AddAsync(child); // fixes
            
            parent.Child = child;
            await context.SaveChangesAsync();
        }
    }
}