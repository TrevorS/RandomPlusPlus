# RandomPlusPlus
#
# Everything here shells out to the scripts in Tools/, so `make` and CI run the
# same commands. Written for the make that ships with macOS (GNU make 3.81), so
# nothing here needs a newer one.

.DEFAULT_GOAL := help
SHELL := /bin/bash

DOTNET := $(shell command -v dotnet 2>/dev/null)

.PHONY: help build test bench check package install uninstall icon format clean

help: ## Show this help
	@echo "RandomPlusPlus"
	@echo
	@grep -E '^[a-z-]+:.*?## .*$$' $(MAKEFILE_LIST) \
	  | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[1m%-12s\033[0m %s\n", $$1, $$2}'
	@echo
	@echo "  First time on a new machine: make build, then make install."
ifeq ($(DOTNET),)
	@echo
	@echo "  NOTE: dotnet is not on PATH. Install the .NET SDK 8.0 or later:"
	@echo "        macOS   brew install --cask dotnet-sdk"
	@echo "        or      https://dotnet.microsoft.com/download"
endif

build: ## Build every supported RimWorld version and verify each
	@./Tools/build.sh

test: ## Run the filter and reroll tests
	@dotnet run --project Tools/RandomPlus.Tests -c Release

bench: ## Measure per-candidate cost and allocations
	@dotnet run --project Tools/RandomPlus.Bench -c Release

check: format-check build test ## Everything CI runs
	@command -v shellcheck >/dev/null && shellcheck Tools/*.sh || \
	  echo "  (shellcheck not installed - skipped. brew install shellcheck)"
	@echo "all checks passed"

package: build ## Build the release zip and validate it
	@./Tools/package.sh

install: build ## Copy the mod into RimWorld's Mods folder
	@./Tools/install.sh

uninstall: ## Remove the mod from RimWorld's Mods folder
	@./Tools/install.sh --uninstall

icon: ## Re-render the mod icon from assets/ModIcon.svg
	@python3 Tools/render-icon.py

format: ## Reformat sources in place
	@dotnet format whitespace RandomPlus.csproj
	@for p in Tools/RandomPlus.Verify Tools/RandomPlus.Tests Tools/RandomPlus.Bench; do \
	  dotnet format whitespace "$$p"; \
	done

format-check:
	@dotnet format whitespace RandomPlus.csproj --verify-no-changes
	@for p in Tools/RandomPlus.Verify Tools/RandomPlus.Tests Tools/RandomPlus.Bench; do \
	  dotnet format whitespace "$$p" --verify-no-changes; \
	done

clean: ## Remove build output
	@rm -rf obj dist Tools/*/obj Tools/*/bin
	@echo "cleaned (Resources/*/Assemblies kept - they are committed)"
