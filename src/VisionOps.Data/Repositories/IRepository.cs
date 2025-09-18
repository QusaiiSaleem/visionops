using System.Linq.Expressions;

namespace VisionOps.Data.Repositories;

/// <summary>
/// Generic repository interface for data access
/// </summary>
public interface IRepository<TEntity> where TEntity : class
{
    /// <summary>
    /// Get entity by ID
    /// </summary>
    Task<TEntity?> GetByIdAsync<TId>(TId id) where TId : notnull;

    /// <summary>
    /// Get all entities
    /// </summary>
    Task<IEnumerable<TEntity>> GetAllAsync();

    /// <summary>
    /// Find entities matching a predicate
    /// </summary>
    Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate);

    /// <summary>
    /// Get a single entity matching a predicate
    /// </summary>
    Task<TEntity?> SingleOrDefaultAsync(Expression<Func<TEntity, bool>> predicate);

    /// <summary>
    /// Add a new entity
    /// </summary>
    Task<TEntity> AddAsync(TEntity entity);

    /// <summary>
    /// Add multiple entities
    /// </summary>
    Task<int> AddRangeAsync(IEnumerable<TEntity> entities);

    /// <summary>
    /// Update an entity
    /// </summary>
    Task UpdateAsync(TEntity entity);

    /// <summary>
    /// Update multiple entities
    /// </summary>
    Task UpdateRangeAsync(IEnumerable<TEntity> entities);

    /// <summary>
    /// Delete an entity
    /// </summary>
    Task DeleteAsync(TEntity entity);

    /// <summary>
    /// Delete entities matching a predicate
    /// </summary>
    Task<int> DeleteWhereAsync(Expression<Func<TEntity, bool>> predicate);

    /// <summary>
    /// Count entities
    /// </summary>
    Task<int> CountAsync();

    /// <summary>
    /// Count entities matching a predicate
    /// </summary>
    Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate);

    /// <summary>
    /// Check if any entity exists matching a predicate
    /// </summary>
    Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate);

    /// <summary>
    /// Save changes to the database
    /// </summary>
    Task<int> SaveChangesAsync();
}