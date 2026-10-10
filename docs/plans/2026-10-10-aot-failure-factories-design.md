# AOT-safe failure results in the Validation and Authorization behaviours

Fixes ZeroAlloc.Mediator#283.

## Problem

Under Native AOT, `ValidationBehavior` and `AuthorizationBehavior` throw `ValidationFailedException` and
`AuthorizationDeniedException` when the response is `Result<T, ValidationError>` or
`Result<T, AuthorizationFailure>`. On JIT, the same requests get a failed `Result` back.

- `FailureFactory<TResponse>` and `AuthorizationFailureFactory<TResponse>` find `Result<,>.Failure` with
  `GetMethod` and `CreateDelegate`.
- The callers suppress IL2091 with `UnconditionalSuppressMessage`, claiming trimming roots preserve
  `Failure`. Under full trimming nothing roots it for the app's own `Result<T, …>`, so `GetMethod` returns
  null and the behaviour falls through to its throw path.
- The publish prints no warning, because of the suppression. AotSmoke hides the Authorization case with a
  `DynamicDependency` and has no validation-failure case.

## Decision

The type that knows how to build a failure is the result type itself, so the knowledge moves there.
ZeroAlloc.Results gains an interface that builds a failure from an error. The behaviours check once per
response type whether `TResponse` implements it. No reflection, no suppression, no generator change, and
it works for every caller of `Handle`, including code that calls it directly.

Rejected: generator-emitted factory registration. The core generator would hard-code two add-on
packages' type names, both packages would need a public registration API, and a `Handle` call the
generator didn't see would still throw under AOT.

## ZeroAlloc.Results 1.4.0

```csharp
public interface IFailureFactory<TSelf, E>
{
    TSelf CreateFailure(E error);
}
```

- Implemented explicitly by `Result<T, E>` as `IFailureFactory<Result<T, E>, E>`, by `Result<T>` as
  `IFailureFactory<Result<T>, string>`, and by `UnitResult<E>` as `IFailureFactory<UnitResult<E>, E>`.
  Each delegates to the type's existing static `Failure`. Explicit implementation keeps the structs'
  visible members unchanged.
- Invariant in both type parameters: the error type must match exactly, as the reflection code does
  today.
- `[EditorBrowsable(Never)]` and XML docs like `IResult`: never a variable or return type, because that
  boxes. Its purpose is a one-time generic check such as
  `default(TResponse) is IFailureFactory<TResponse, TError>`.
- The method ignores the instance it is called on; the docs say so.
- A `feat:` commit, so release-please cuts a minor.

## ZeroAlloc.Mediator

- `FailureFactory<TResponse>` becomes:

  ```csharp
  internal static class FailureFactory<TResponse>
  {
      internal static readonly Func<ValidationError, TResponse>? Create =
          default(TResponse) is IFailureFactory<TResponse, ValidationError> factory
              ? factory.CreateFailure
              : null;
  }
  ```

  `AuthorizationFailureFactory<TResponse>` does the same with `AuthorizationFailure`. The
  `DynamicallyAccessedMembers` annotations and `System.Reflection` usings go.
- The value-type `default` boxes once, in the static initializer, never on the request path. A
  reference-type `TResponse` has a null default, so `Create` is null and the behaviour throws as today.
- Both `UnconditionalSuppressMessage` attributes and their comment blocks are removed. The trim analyzer
  then checks both packages for real.
- `ZeroAlloc.Mediator.Validation` and `ZeroAlloc.Mediator.Authorization` raise their `ZeroAlloc.Results`
  floor to `1.4.0`.

### Behaviour change

Requests whose response is `UnitResult<ValidationError>` or `UnitResult<AuthorizationFailure>` now get a
failed result instead of an exception, on JIT and AOT alike. `Result<T>` never matches, because its error
type is `string`. This goes in the PR's "Behaviour change" section for the release notes.

## Tests

- **Results:** each of the three types builds a failure through the interface with the given error, and
  the interface is not reachable as a public member of the struct.
- **Mediator unit tests:** for each behaviour, `Result<T, E>` and `UnitResult<E>` responses return a
  failure carrying the error, and a plain response still throws.
- **AotSmoke:** remove the `DynamicDependency`. Add validation-failure and authorization-deny cases for
  `Result<int, …>`, `Result<record class, …>`, `Result<record struct, …>` and `UnitResult<…>`, each
  asserting a failed result. CI's AOT publish then catches a regression.
- A local Native AOT probe run against the fix confirms all cases return failures with no trim warning.

## Sequencing

1. Results PR, merged when green.
2. One-off Results 1.4.0 release, on the maintainer's call.
3. Mediator PR against the published 1.4.0. Until then, verify locally against a `9.9.9-local.*` pack.
4. Confirm both `feat:` and `fix:` lines appear in Mediator's release-please PR.
