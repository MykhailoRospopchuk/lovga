namespace LovgaBroker.Services;

using Dapper;
using Models;
using Storage;

public class StorageService
{
    private readonly DataContext _context;
    private readonly ILogger<StorageService> _logger;

    public StorageService(DataContext context, ILogger<StorageService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<int> InsertMessage(Message message)
    {
        var command = 
@"
INSERT INTO MessageEntity (Topic, Content, CreatedAt)
VALUES (@Topic, @Content, @CreatedAt)
RETURNING Id;
";

        try
        {
            using var connection = _context.CreateConnection();

            var result = await connection.QuerySingleAsync<int>(command, message);

            return result;
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            return -1;
        }
    }

    public async Task DeleteMessage(int id)
    {
        var command = 
@"
DELETE FROM MessageEntity WHERE Id = @Id;
";

        try
        {
            using var connection = _context.CreateConnection();

            var result = await connection.ExecuteAsync(command, new { Id = id });
            if (result == 0)
            {
                _logger.LogInformation($"Message with id {id} has NOT been deleted.");
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
        }
    }
}