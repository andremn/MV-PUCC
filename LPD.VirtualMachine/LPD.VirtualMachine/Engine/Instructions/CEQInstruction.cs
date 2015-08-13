﻿using static LPD.VirtualMachine.Engine.InstructionSet;

namespace LPD.VirtualMachine.Engine.Instructions
{
    [Instruction(CEQ)]
    class CEQInstruction : IncrementalInstruction
    {
        protected override void SpecificExecute(ExecutionContext context, int[] parameters)
        {
            int first;
            int second;
            Stack stack = context.Memory.StackRegion;

            first = stack.Load();
            stack.Down();
            second = stack.Load();

            if (second == first)
            {
                second = 1;
            }
            else
            {
                second = 0;
            }

            stack.Store(second);
        }
    }
}
