using System.Collections.Concurrent;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MeetingRooms.Api.Filters;

/// <summary>
/// Runs the FluentValidation validator for every bound action argument that has one, and
/// short-circuits with a 400 on the first invalid argument.
/// <para>
/// An action filter rather than middleware: middleware runs before MVC has bound anything, so
/// at that point there is no DTO to hand a validator. This is the earliest interception point
/// that has the action's arguments. (A false friend for anyone arriving from Laravel, where
/// middleware does see a parsed request.)
/// </para>
/// <para>
/// One registration covers every endpoint, so no controller can forget to validate.
/// </para>
/// </summary>
public sealed class AsyncValidationFilter : IAsyncActionFilter
{
    /// <summary>
    /// Closed <c>IValidator&lt;T&gt;</c> types, cached by argument type. Without this, every
    /// argument of every request pays for a reflective type construction.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Type> ValidatorTypesByArgumentType = new();

    private readonly IServiceProvider _serviceProvider;
    private readonly ProblemDetailsFactory _problemDetailsFactory;

    public AsyncValidationFilter(IServiceProvider serviceProvider, ProblemDetailsFactory problemDetailsFactory)
    {
        _serviceProvider = serviceProvider;
        _problemDetailsFactory = problemDetailsFactory;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            // Resolution is by the argument's *concrete* type. A validator registered for a
            // base type will not fire for a type derived from it, so every concrete request
            // type needs its own validator - sharing rules is what Include() is for.
            var validatorType = ValidatorTypesByArgumentType.GetOrAdd(
                argument.GetType(),
                static argumentType => typeof(IValidator<>).MakeGenericType(argumentType));

            if (_serviceProvider.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationResult = await validator.ValidateAsync(
                new ValidationContext<object>(argument),
                context.HttpContext.RequestAborted);

            if (validationResult.IsValid)
            {
                continue;
            }

            var modelState = new ModelStateDictionary();

            foreach (var failure in validationResult.Errors)
            {
                modelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            // Built by MVC's own factory, so a hand-rolled validation failure is byte-for-byte
            // the shape [ApiController] produces for a model-binding failure. Identical by
            // construction rather than by keeping a second template in sync.
            context.Result = new BadRequestObjectResult(
                _problemDetailsFactory.CreateValidationProblemDetails(context.HttpContext, modelState));

            return;
        }

        await next();
    }
}
