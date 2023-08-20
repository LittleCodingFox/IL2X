#include "System_String.hpp"

struct StringInstance
{
	void* runtimeType;
	IL2X_CoreLib_System_Int32 f__stringLength;
	IL2X_CoreLib_System_Char f__firstChar;
};

IL2X_CoreLib_System_Int32 IL2X_CoreLib_System_String::get_Length()
{
	return f__stringLength;
}

IL2X_CoreLib_System_String* IL2X_CoreLib_System_String::FastAllocateString(IL2X_CoreLib_System_Int32 p_length)
{
	auto sizeLength = sizeof(StringInstance) + (p_length + 2) * sizeof(IL2X_CoreLib_System_Char);

	auto buffer = malloc(sizeLength);

	memset(buffer, 0, sizeLength);

	auto instance = (StringInstance*)buffer;

	instance->f__stringLength = p_length;

	return (IL2X_CoreLib_System_String*)buffer;
}
