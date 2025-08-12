#!/bin/bash

dotnet run -- --runs 30 --scenario Empty --all-variations --compiler msvc
dotnet run -- --runs 30 --scenario Empty --all-variations --compiler clang
dotnet run -- --runs 30 -n 1000,2000,4000,8000,16000 --scenario Funcs --all-variations --compiler msvc
dotnet run -- --runs 30 -n 1000,2000,4000,8000,16000 --scenario Funcs --all-variations --compiler clang
dotnet run -- --runs 30 -n 1000,2000,4000,8000,16000 --scenario CppMember --all-variations --compiler msvc
dotnet run -- --runs 30 -n 1000,2000,4000,8000,16000 --scenario CppMember --all-variations --compiler clang
dotnet run -- --runs 30 -n 1000,2000,4000,8000,16000 --scenario FreeFunc --all-variations --compiler msvc
dotnet run -- --runs 30 -n 1000,2000,4000,8000,16000 --scenario FreeFunc --all-variations --compiler clang
dotnet run -- --runs 30 -n 1000,2000,4000 --scenario CppOverload --all-variations --compiler msvc
dotnet run -- --runs 30 -n 1000,2000,4000 --scenario CppOverload --all-variations --compiler clang
dotnet run -- --runs 30 -n 1000,2000,4000,8000,16000 --scenario NoOverload --all-variations --compiler msvc
dotnet run -- --runs 30 -n 1000,2000,4000,8000,16000 --scenario NoOverload --all-variations --compiler clang
dotnet run -- --runs 30 -n 1000,2000,4000,8000,16000 --scenario ReturnByPointer --all-variations --compiler msvc
dotnet run -- --runs 30 -n 1000,2000,4000,8000,16000 --scenario ReturnByPointer --all-variations --compiler clang
dotnet run -- --runs 30 -n 1000,2000,4000,8000,16000 --scenario ReturnByValue --all-variations --compiler msvc
dotnet run -- --runs 30 -n 1000,2000,4000,8000,16000 --scenario ReturnByValue --all-variations --compiler clang