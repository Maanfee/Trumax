using Maanfee.Web.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Data;
using System.Text.RegularExpressions;
using Trumax.View.ViewModels;

namespace Trumax.Services.Controllers
{
    [Route("api/[controller]")]
    [ApiController] 
    //[Authorize]
    //[ApiExplorerSettings(IgnoreApi = true)]
    public class SqlServerdbManagerController : ControllerBase
    {
        public SqlServerdbManagerController(IConfiguration configuration, ILogger<SqlServerdbManagerController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        private readonly IConfiguration _configuration;
        // ساده‌سازی‌شده: در Production بهتر است از IMemoryCache یا Redis استفاده شود
        // (این Dictionary فقط روی یک اینستنس از اپ کار می‌کند)
        private static readonly ConcurrentDictionary<string, BackupJobStatus> Jobs = new();
        private static readonly ConcurrentDictionary<string, SqlConnectionStringBuilder> JobConnections = new();
        private readonly ILogger<SqlServerdbManagerController>? _logger;

        [HttpPost("TestConnection")]
        // POST: api/SqlServerdbManager/TestConnection
        public async Task<ActionResult<CallbackResult<string>>> TestConnection([FromBody] Login ViewModel)
        {
            try
            {
                var builder = new SqlConnectionStringBuilder();

                if (!string.IsNullOrEmpty(ViewModel.ServerName))
                {
                    builder.DataSource = ViewModel.ServerName;
                }

                if (ViewModel.IdAuthentication == 1)
                {
                    builder.IntegratedSecurity = true;
                }
                else
                {
                    builder.UserID = ViewModel.UserName;
                    builder.Password = ViewModel.Password;
                    builder.IntegratedSecurity = false;
                }

                builder.TrustServerCertificate = true;
                builder.ConnectTimeout = 30;

                using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync();

                return new CallbackResult<string>(builder.ConnectionString, null);
            }
            catch (Exception ex)
            {
                return new CallbackResult<string>(null, new ExceptionError(ex.Message));
            }
        }

        [HttpPost("GetDatabases")]
        public async Task<ActionResult<CallbackResult<List<TreeNode>>>> GetDatabases([FromBody] Login ViewModel)
        {
            try
            {
                var builder = BuildConnectionString(ViewModel);
                using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync();

                var list = new List<TreeNode>();
                const string query = "SELECT name FROM sys.databases WHERE database_id > 4 ORDER BY name";

                using var command = new SqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var dbName = reader.GetString(0);
                    list.Add(new TreeNode
                    {
                        Id = dbName,
                        Text = dbName,
                        NodeType = TreeNodeType.Database,
                        HasChildren = true
                    });
                }

                return new CallbackResult<List<TreeNode>>(list, null);
            }
            catch (Exception ex)
            {
                return new CallbackResult<List<TreeNode>>(null, new ExceptionError(ex.Message));
            }
        }

        [HttpPost("GetTables")]
        public async Task<ActionResult<CallbackResult<List<TreeNode>>>> GetTables([FromBody] SchemaRequest ViewModel)
        {
            try
            {
                var builder = BuildConnectionString(ViewModel);
                builder.InitialCatalog = ViewModel.DatabaseName;

                using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync();

                var list = new List<TreeNode>();
                const string query = @"
            SELECT TABLE_SCHEMA, TABLE_NAME 
            FROM INFORMATION_SCHEMA.TABLES 
            WHERE TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_SCHEMA, TABLE_NAME";

                using var command = new SqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var schema = reader.GetString(0);
                    var table = reader.GetString(1);
                    list.Add(new TreeNode
                    {
                        Id = $"{ViewModel.DatabaseName}|{schema}.{table}",
                        Text = $"{schema}.{table}",
                        NodeType = TreeNodeType.Table,
                        HasChildren = true
                    });
                }

                return new CallbackResult<List<TreeNode>>(list, null);
            }
            catch (Exception ex)
            {
                return new CallbackResult<List<TreeNode>>(null, new ExceptionError(ex.Message));
            }
        }

        [HttpPost("GetColumns")]
        public async Task<ActionResult<CallbackResult<List<TreeNode>>>> GetColumns([FromBody] SchemaRequest ViewModel)
        {
            try
            {
                var builder = BuildConnectionString(ViewModel);
                builder.InitialCatalog = ViewModel.DatabaseName;

                using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync();

                var parts = ViewModel.TableName!.Split('.'); // Schema.Table
                var list = new List<TreeNode>();
                const string query = @"
            SELECT COLUMN_NAME, DATA_TYPE 
            FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @Table
            ORDER BY ORDINAL_POSITION";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@Schema", parts[0]);
                command.Parameters.AddWithValue("@Table", parts[1]);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var colName = reader.GetString(0);
                    var dataType = reader.GetString(1);
                    list.Add(new TreeNode
                    {
                        Id = $"{ViewModel.DatabaseName}|{ViewModel.TableName}|{colName}",
                        Text = $"{colName} ({dataType})",
                        NodeType = TreeNodeType.Column,
                        HasChildren = false
                    });
                }

                return new CallbackResult<List<TreeNode>>(list, null);
            }
            catch (Exception ex)
            {
                return new CallbackResult<List<TreeNode>>(null, new ExceptionError(ex.Message));
            }
        }

        [HttpPost("GetDatabaseSize")]
        public async Task<ActionResult<CallbackResult<DatabaseSizeInfo>>> GetDatabaseSize([FromBody] SchemaRequest ViewModel)
        {
            try
            {
                var builder = BuildConnectionString(ViewModel);
                using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync();

                var info = await GetDatabaseSizeInternal(connection, ViewModel.DatabaseName);
                return new CallbackResult<DatabaseSizeInfo>(info, null);
            }
            catch (Exception ex)
            {
                return new CallbackResult<DatabaseSizeInfo>(null, new ExceptionError(ex.Message));
            }
        }

        [HttpPost("ShrinkLogFile")]
        public async Task<ActionResult<CallbackResult<DatabaseSizeInfo>>> ShrinkLogFile([FromBody] ShrinkLogRequest ViewModel)
        {
            try
            {
                var builder = BuildConnectionString(ViewModel);
                builder.InitialCatalog = ViewModel.DatabaseName; // باید کانتکست روی همین دیتابیس باشد

                using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync();

                // ۱) گرفتن نام منطقی فایل لاگ به صورت پویا
                string? logicalLogName;
                const string getLogNameQuery = @"
            SELECT name 
            FROM sys.master_files 
            WHERE database_id = DB_ID(@DatabaseName) AND type_desc = 'LOG'";

                using (var cmd = new SqlCommand(getLogNameQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@DatabaseName", ViewModel.DatabaseName);
                    logicalLogName = (string?)await cmd.ExecuteScalarAsync();
                }

                if (string.IsNullOrEmpty(logicalLogName))
                {
                    return new CallbackResult<DatabaseSizeInfo>(null, new ExceptionError("Log file not found."));
                }

                // ۲) اجرای مراحل Shrink
                // نام دیتابیس و فایل را نمی‌توان Parameterize کرد، پس escape می‌کنیم (] -> ]])
                var safeDbName = ViewModel.DatabaseName!.Replace("]", "]]");
                var safeLogName = logicalLogName.Replace("'", "''");

                var commands = new[]
                {
            $"ALTER DATABASE [{safeDbName}] SET RECOVERY SIMPLE;",
            $"DBCC SHRINKFILE (N'{safeLogName}', {ViewModel.TargetSizeMB});",
            $"ALTER DATABASE [{safeDbName}] SET RECOVERY FULL;"
        };

                foreach (var sql in commands)
                {
                    using var cmd = new SqlCommand(sql, connection)
                    {
                        CommandTimeout = 300
                    };
                    await cmd.ExecuteNonQueryAsync();
                }

                // ۳) گرفتن دوبارهٔ حجم فایل‌ها بعد از Shrink
                var sizeInfo = await GetDatabaseSizeInternal(connection, ViewModel.DatabaseName);

                return new CallbackResult<DatabaseSizeInfo>(sizeInfo, null);
            }
            catch (Exception ex)
            {
                return new CallbackResult<DatabaseSizeInfo>(null, new ExceptionError(ex.Message));
            }
        }

        [HttpPost("StartBackup")]
        public ActionResult<CallbackResult<string>> StartBackup([FromBody] SchemaRequest ViewModel)
        {
            try
            {
                var jobId = Guid.NewGuid().ToString("N");
                var builder = BuildConnectionString(ViewModel);

                Jobs[jobId] = new BackupJobStatus
                {
                    DatabaseName = ViewModel.DatabaseName,
                    PercentComplete = 0
                };
                JobConnections[jobId] = builder;

                // اجرای بک‌آپ در پس‌زمینه - Request فوراً برمی‌گردد
                _ = Task.Run(() => RunBackupJob(jobId, builder, ViewModel.DatabaseName!));

                return new CallbackResult<string>(jobId, null);
            }
            catch (Exception ex)
            {
                return new CallbackResult<string>(null, new ExceptionError(ex.Message));
            }
        }

        [HttpGet("BackupProgress/{jobId}")]
        public ActionResult<CallbackResult<BackupJobStatus>> BackupProgress(string jobId)
        {
            if (Jobs.TryGetValue(jobId, out var job))
                return new CallbackResult<BackupJobStatus>(job, null);

            return NotFound(new CallbackResult<BackupJobStatus>(null, new ExceptionError("Job not found.")));
        }
              
        [HttpGet("BackupDownload/{jobId}")]
        public async Task BackupDownload(string jobId)
        {
            if (!Jobs.TryGetValue(jobId, out var job) || !job.Completed || job.TempFilePaths.Count == 0)
            {
                Response.StatusCode = 404;
                return;
            }

            if (!JobConnections.TryGetValue(jobId, out var builder))
            {
                Response.StatusCode = 404;
                return;
            }

            // فعال کردن IO همزمان فقط برای این Request - چون ZipArchive.Dispose() به آن نیاز دارد
            var syncIOFeature = HttpContext.Features.Get<IHttpBodyControlFeature>();
            if (syncIOFeature != null)
            {
                syncIOFeature.AllowSynchronousIO = true;
            }

            Response.ContentType = "application/zip";
            Response.Headers.Append("Content-Disposition",
                $"attachment; filename=\"{job.DatabaseName}_backup.zip\"");

            var bodyFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
            bodyFeature?.DisableBuffering();

            try
            {
                using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync();

                using (var zipArchive = new System.IO.Compression.ZipArchive(
                    Response.Body, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var stripePath in job.TempFilePaths)
                    {
                        var entryName = Path.GetFileName(stripePath);
                        var entry = zipArchive.CreateEntry(entryName, System.IO.Compression.CompressionLevel.NoCompression);

                        var safePath = stripePath.Replace("'", "''");
                        var query = $"SELECT BulkColumn FROM OPENROWSET(BULK '{safePath}', SINGLE_BLOB) AS x";

                        using var command = new SqlCommand(query, connection) { CommandTimeout = 0 };
                        using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);

                        if (await reader.ReadAsync())
                        {
                            using var blobStream = reader.GetStream(0);
                            using var entryStream = entry.Open();
                            await blobStream.CopyToAsync(entryStream, bufferSize: 81920);
                        }

                        await Response.Body.FlushAsync();
                    }
                } // اینجا Dispose صدا زده می‌شود و حالا چون AllowSynchronousIO=true است، خطا نمی‌دهد

                await Response.Body.FlushAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Backup download failed for job {JobId}", jobId);
                return;
            }

            try
            {
                using var cleanupConnection = new SqlConnection(builder.ConnectionString);
                await cleanupConnection.OpenAsync();

                foreach (var stripePath in job.TempFilePaths)
                {
                    var safePath = stripePath.Replace("'", "''");
                    var cleanupQuery = $"EXEC master.sys.xp_delete_file 0, N'{safePath}'";
                    using var cleanupCmd = new SqlCommand(cleanupQuery, cleanupConnection);
                    await cleanupCmd.ExecuteNonQueryAsync();
                }
            }
            catch { }

            Jobs.TryRemove(jobId, out _);
            JobConnections.TryRemove(jobId, out _);
        }

        [HttpPost("ExecuteQuery")]
        public async Task<ActionResult<CallbackResult<QueryResult>>> ExecuteQuery([FromBody] QueryRequest ViewModel)
        {
            // لایهٔ دفاعی اول: رد کردن هر چیزی غیر از یک دستور SELECT ساده
            if (!IsReadOnlySelect(ViewModel.QueryText, out var rejectReason))
            {
                return new CallbackResult<QueryResult>(null, new ExceptionError(rejectReason!));
            }

            try
            {
                var builder = BuildConnectionString(ViewModel);
                builder.InitialCatalog = ViewModel.DatabaseName;

                using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync();

                // لایهٔ دفاعی دوم: اجرای کوئری داخل تراکنشی که همیشه Rollback می‌شود
                // حتی اگر لایهٔ اول به هر دلیلی دور زده شود، هیچ تغییری در دیتابیس ماندگار نمی‌شود
                using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var result = new QueryResult();

                try
                {
                    using var command = new SqlCommand(ViewModel.QueryText, connection, transaction)
                    {
                        CommandTimeout = 30 // لایهٔ دفاعی سوم: جلوگیری از کوئری‌های سنگین/بی‌پایان
                    };

                    using var reader = await command.ExecuteReaderAsync();

                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        result.Columns.Add(reader.GetName(i));
                    }

                    const int MaxRows = 5000; // سقف تعداد ردیف برای جلوگیری از پاسخ‌های حجیم
                    while (await reader.ReadAsync())
                    {
                        if (result.Rows.Count >= MaxRows)
                        {
                            result.Truncated = true;
                            break;
                        }

                        var row = new List<object?>(reader.FieldCount);
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row.Add(ConvertValue(reader.GetValue(i)));
                        }
                        result.Rows.Add(row);
                    }
                }
                finally
                {
                    // چون فقط SELECT مجاز است اصولاً چیزی برای Commit وجود ندارد؛
                    // Rollback هم به‌عنوان یک تضمین اضافه و هم برای آزاد کردن قفل‌ها زده می‌شود
                    try { transaction.Rollback(); } catch { }
                }

                stopwatch.Stop();
                result.ElapsedMs = stopwatch.ElapsedMilliseconds;

                return new CallbackResult<QueryResult>(result, null);
            }
            catch (Exception ex)
            {
                return new CallbackResult<QueryResult>(null, new ExceptionError(ex.Message));
            }
        }

        // ***************************************

        private SqlConnectionStringBuilder BuildConnectionString(Login ViewModel)
        {
            var builder = new SqlConnectionStringBuilder();

            if (!string.IsNullOrEmpty(ViewModel.ServerName))
                builder.DataSource = ViewModel.ServerName;

            if (ViewModel.IdAuthentication == 1)
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                builder.UserID = ViewModel.UserName;
                builder.Password = ViewModel.Password;
                builder.IntegratedSecurity = false;
            }

            builder.TrustServerCertificate = true;
            builder.ConnectTimeout = 30;

            return builder;
        }

        private async Task<DatabaseSizeInfo> GetDatabaseSizeInternal(SqlConnection connection, string? databaseName)
        {
            const string query = @"
        SELECT type_desc, CAST(size AS BIGINT) AS SizeInPages
        FROM sys.master_files
        WHERE database_id = DB_ID(@DatabaseName)";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@DatabaseName", databaseName);

            double dataSizeMB = 0, logSizeMB = 0;

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var typeDesc = reader.GetString(0);
                var pages = reader.GetInt64(1);
                var sizeMB = pages * 8.0 / 1024.0;

                if (typeDesc == "LOG")
                    logSizeMB += sizeMB;
                else
                    dataSizeMB += sizeMB;
            }

            return new DatabaseSizeInfo
            {
                DataSizeMB = Math.Round(dataSizeMB, 2),
                LogSizeMB = Math.Round(logSizeMB, 2),
                TotalSizeMB = Math.Round(dataSizeMB + logSizeMB, 2)
            };
        }

        private const string BackupRootFolder = @"D:\TrumaxDatabaseBackup";
        private const double MaxStripeSizeMB = 1400; // مقداری امن، زیر سقف ۲۰۴۸MB با حاشیهٔ اطمینان

        private async Task RunBackupJob(string jobId, SqlConnectionStringBuilder builder, string databaseName)
        {
            var job = Jobs[jobId];

            try
            {
                using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync();

                using (var mkdirCmd = new SqlCommand("EXEC master.sys.xp_create_subdir @Path", connection))
                {
                    mkdirCmd.Parameters.AddWithValue("@Path", BackupRootFolder);
                    await mkdirCmd.ExecuteNonQueryAsync();
                }

                // تخمین حجم دیتابیس برای محاسبهٔ تعداد فایل‌های تقسیم‌شده
                var sizeInfo = await GetDatabaseSizeInternal(connection, databaseName);
                var estimatedMB = sizeInfo.TotalSizeMB;
                var stripeCount = Math.Max(1, (int)Math.Ceiling(estimatedMB / MaxStripeSizeMB));

                var stripePaths = new List<string>();
                for (int i = 1; i <= stripeCount; i++)
                {
                    var fileName = $"{jobId}_part{i}.bak";
                    stripePaths.Add(Path.Combine(BackupRootFolder, fileName));
                }
                job.TempFilePaths = stripePaths;

                connection.InfoMessage += (s, e) =>
                {
                    foreach (SqlError error in e.Errors)
                    {
                        var match = Regex.Match(error.Message, @"(\d+)\s*percent");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
                        {
                            job.PercentComplete = percent;
                        }
                    }
                };

                var safeDbName = databaseName.Replace("]", "]]");
                var diskClauses = string.Join(", ", stripePaths.Select((p, i) => $"DISK = @Path{i}"));

                var query = $@"
            BACKUP DATABASE [{safeDbName}] 
            TO {diskClauses}
            WITH INIT, FORMAT, COMPRESSION, STATS = 5";

                using var command = new SqlCommand(query, connection) { CommandTimeout = 0 };
                for (int i = 0; i < stripePaths.Count; i++)
                {
                    command.Parameters.AddWithValue($"@Path{i}", stripePaths[i]);
                }

                await command.ExecuteNonQueryAsync();

                job.PercentComplete = 100;
                job.Completed = true;
            }
            catch (Exception ex)
            {
                job.Failed = true;
                job.ErrorMessage = ex.Message;
            }
        }

        private static bool IsReadOnlySelect(string? sql, out string? reason)
        {
            reason = null;

            if (string.IsNullOrWhiteSpace(sql))
            {
                reason = "Query is empty.";
                return false;
            }

            // حذف کامنت‌های بلوکی و خطی، چون می‌توان دستورات ممنوعه را داخل آن‌ها پنهان کرد
            var cleaned = Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            cleaned = Regex.Replace(cleaned, @"--.*?$", " ", RegexOptions.Multiline);

            var trimmed = cleaned.Trim();
            var withoutTrailingSemicolon = trimmed.TrimEnd().TrimEnd(';').TrimEnd();

            // اجازهٔ چند دستور پشت سرهم داده نمی‌شود (جلوگیری از الگوی "SELECT 1; DROP TABLE ...")
            if (withoutTrailingSemicolon.Contains(';'))
            {
                reason = "Multiple statements are not allowed. Please run a single SELECT statement.";
                return false;
            }

            if (Regex.IsMatch(withoutTrailingSemicolon, @"(^|\s)GO(\s|$)", RegexOptions.IgnoreCase))
            {
                reason = "GO batches are not allowed.";
                return false;
            }

            // دستور باید فقط با SELECT یا WITH (برای CTE) شروع شود
            if (!Regex.IsMatch(withoutTrailingSemicolon.TrimStart(), @"^\s*(SELECT|WITH)\b", RegexOptions.IgnoreCase))
            {
                reason = "Only SELECT statements are allowed.";
                return false;
            }

            foreach (var keyword in ForbiddenKeywords)
            {
                var pattern = keyword.EndsWith("_")
                    ? Regex.Escape(keyword)          // sp_ / xp_ به‌عنوان پیشوند بررسی می‌شود
                    : $@"\b{Regex.Escape(keyword)}\b"; // بقیه به‌عنوان کلمهٔ کامل

                if (Regex.IsMatch(withoutTrailingSemicolon, pattern, RegexOptions.IgnoreCase))
                {
                    reason = $"Keyword '{keyword.TrimEnd('_')}' is not allowed in read-only queries.";
                    return false;
                }
            }

            return true;
        }

        private static object? ConvertValue(object value)
        {
            if (value == null || value == DBNull.Value)
                return null;

            if (value is byte[] bytes)
                return $"0x{Convert.ToHexString(bytes)}";

            return value;
        }

        // کلماتی که در صورت وجود در متن کوئری، درخواست را رد می‌کنند
        private static readonly string[] ForbiddenKeywords = new[]
        {
            "INSERT", "UPDATE", "DELETE", "MERGE", "DROP", "ALTER", "TRUNCATE",
            "CREATE", "EXEC", "EXECUTE", "GRANT", "REVOKE", "DENY",
            "BACKUP", "RESTORE", "SHUTDOWN", "DBCC",
            "OPENROWSET", "OPENQUERY", "OPENDATASOURCE", "BULK", "INTO",
            "sp_", "xp_"
        };

    }
}
