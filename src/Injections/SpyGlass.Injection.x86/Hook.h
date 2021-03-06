#pragma once

#include <vector>

/**
 * Specifies parameters for setting hooks.
 */
struct HookParameters
{
	void* Address;
	int BytesToOverwrite;
	std::vector<int> OffsetsNeedingFixup;
};

#define REGISTER_COUNT 9

#define REGISTER_EAX 0
#define REGISTER_ECX 1
#define REGISTER_EDX 2
#define REGISTER_EBX 3
#define REGISTER_ESP 4
#define REGISTER_EBP 5
#define REGISTER_ESI 6
#define REGISTER_EDI 7
#define REGISTER_EIP 8

/**
 * Defines the function signature of a hook callback.
 * @param registers: A pointer to a collection of register values.
 * @param stack: A pointer to the top of the stack.
 */
typedef void (_stdcall *HookCallBack)(SIZE_T* registers, SIZE_T* stack);

/*
 * Represents a single hook instance that is set at a specific memory location with one callback.
 */
class Hook
{
public:
    Hook(HookParameters parameters, HookCallBack callback);
	~Hook();

	void Set();
	void Unset();

private:
	void ReadBytesToOverwrite();
	void CreateTrampoline();
	void CreateHookBytes();

	HookParameters _parameters;
	HookCallBack _callback;

	bool _isSet;

	char* _originalBytes;
	char* _hookBytes;
	void* _trampoline;
};