using DairyControl.Api.Validation;
using DairyControl.Domain.Exceptions;
using System.Diagnostics;
using System.Text.Json;

namespace DairyControl.Api.Middleware
{
    // Maps the domain exceptions to their HTTP status and logs everything else (with the trace id the client also gets).
    public class ExceptionHandlingMiddleware
    {
        public const string MensajeInesperado = "Ocurrió un error inesperado.";

        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (ReglaDeNegocioException ex)
            {
                await Escribir(context, StatusCodes.Status400BadRequest, ApiErrors.Body(ex.Message, ex.Campo));
            }
            catch (DemasiadosIntentosException ex)
            {
                await Escribir(context, StatusCodes.Status429TooManyRequests, ApiErrors.Body(ex.Message));
            }
            catch (Exception ex) when (ex is TicketDuplicadoException or ConflictoDeConcurrenciaException or ConflictoDeUnicidadException)
            {
                await Escribir(context, StatusCodes.Status409Conflict, ApiErrors.Body(ex.Message));
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The client went away: nothing to answer and nothing wrong on our side
                _logger.LogDebug("Request aborted by the client: {Method} {Path}", context.Request.Method, context.Request.Path.Value);
            }
            catch (Exception ex)
            {
                var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

                // Method and path only: never the body, the query string, headers or tokens
                _logger.LogError(ex, "Unhandled exception on {Method} {Path} (trace {TraceId})",
                    context.Request.Method, context.Request.Path.Value, traceId);

                if (context.Response.HasStarted)
                {
                    throw;
                }
                await Escribir(context, StatusCodes.Status500InternalServerError, ApiErrors.BodyInesperado(MensajeInesperado, traceId));
            }
        }

        private static Task Escribir(HttpContext context, int status, object body)
        {
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync(JsonSerializer.Serialize(body));
        }
    }
}
