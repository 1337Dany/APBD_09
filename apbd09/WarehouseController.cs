using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace apbd09;

[ApiController]
[Route("api/[controller]")]
public class WarehouseController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public WarehouseController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost]
    public async Task<IActionResult> AddProductToWarehouse([FromBody] ProductWarehouseDto dto)
    {
        if (dto.Amount <= 0)
            return BadRequest("Amount must be greater than zero.");

        using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();

        using var transaction = connection.BeginTransaction();

        try
        {
            var cmd = new SqlCommand("SELECT 1 FROM Product WHERE IdProduct = @ProductId", connection, transaction);
            cmd.Parameters.AddWithValue("@ProductId", dto.ProductId);
            if ((await cmd.ExecuteScalarAsync()) == null)
                return NotFound("Product not found.");
            
            cmd = new SqlCommand("SELECT 1 FROM Warehouse WHERE IdWarehouse = @WarehouseId", connection, transaction);
            cmd.Parameters.AddWithValue("@WarehouseId", dto.WarehouseId);
            if ((await cmd.ExecuteScalarAsync()) == null)
                return NotFound("Warehouse not found.");
            
            cmd = new SqlCommand(@"
                SELECT IdOrder FROM [Order] 
                WHERE IdProduct = @ProductId AND Amount = @Amount AND CreatedAt < @CreatedAt", connection, transaction);
            cmd.Parameters.AddWithValue("@Amount", dto.Amount);
            cmd.Parameters.AddWithValue("@CreatedAt", dto.CreatedAt);
            var idOrder = await cmd.ExecuteScalarAsync();

            if (idOrder == null)
                return BadRequest("Matching order not found.");
            
            cmd = new SqlCommand(@"
                SELECT 1 FROM Product_Warehouse 
                WHERE IdOrder = @IdOrder", connection, transaction);
            cmd.Parameters.AddWithValue("@IdOrder", (int)idOrder);
            if ((await cmd.ExecuteScalarAsync()) != null)
                return Conflict("Order already fulfilled.");
            
            cmd = new SqlCommand("UPDATE [Order] SET FulfilledAt = GETDATE() WHERE IdOrder = @IdOrder", connection, transaction);
            cmd.Parameters.AddWithValue("@IdOrder", (int)idOrder);
            await cmd.ExecuteNonQueryAsync();
            
            cmd = new SqlCommand("SELECT Price FROM Product WHERE IdProduct = @ProductId", connection, transaction);
            var price = (decimal)(await cmd.ExecuteScalarAsync());
            
            cmd = new SqlCommand(@"
                INSERT INTO Product_Warehouse (IdWarehouse, IdProduct, IdOrder, Amount, Price, CreatedAt)
                OUTPUT INSERTED.IdProductWarehouse
                VALUES (@WarehouseId, @ProductId, @IdOrder, @Amount, @TotalPrice, GETDATE())", connection, transaction);

            cmd.Parameters.AddWithValue("@TotalPrice", price * dto.Amount);
            var insertedId = (int)(await cmd.ExecuteScalarAsync());

            transaction.Commit();
            return Ok(new { Id = insertedId });
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }
}