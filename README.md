# RetryDemoFunctionApp

## Overview
A .NET 8 Azure Functions app (isolated mode) demonstrating resilient retrying when calling an API that might return a 429 http status code with a retry-after header. In the case of a 429 response, the interval specified in the retry-after header is used to determine when to retry the operation. Other transient failures are retried using a Durable Function retry policy object.

This is a demo app that can be used for learning purposes. It can also be used as a starting point for building more complex Durable Function applications that require robust retry mechanisms.

## Features
- Durable Functions orchestration with retry support, including handling of 429 responses
- Configurable retry options (max attempts, backoff, jitter)
- Activity demonstrating transient failures and retry behavior
- Local emulator function for testing

## Project Structure
- `Program.cs`: Functions host startup and configuration
- `Functions/HttpFunctions.cs`: HTTP triggers to start and get status of orchestrations
- `Orchestrations/MainOrchestrations.cs`: Durable orchestrators applying retry policies and coordinating activities
- `Activities/RetryActivity.cs`: Activity showcasing transient error handling
- `Emulator/EmulatorFunction.cs`: Emulator helper for local testing

## Requirements
- .NET 8 SDK
- Azure Functions Core Tools
- Azurite (optional) for local storage emulation`

## Notes
- Intended for demonstration and learning of durable retries with transient failures.
- For production, validate inputs, secure HTTP endpoints, and externalize configuration.