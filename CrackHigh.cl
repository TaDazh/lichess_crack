__constant long multiplier = 0x5DEECE66DL;
__constant long inv_mult = 0xDFE05BCB1365L;
__constant int increment = 0xB;
__constant long mask = ((1L << 48) - 1);

__kernel void CrackHigh(global int* sequence, global long* seeds, long low)
{
	if (seeds[0] != 0)
	{
		return;
	}

	int idx = get_global_id(0);

    int threadCount = get_global_size(0);
    long idxCount = (1L << 31) / threadCount;

	__local int localSeq[16];
	for (int k = 0; k < 16; k++)
	{
		localSeq[k] = sequence[k];
	}

	if (idx >= 0 && idx < threadCount)
	{
		long highStart = idx * idxCount;
		long highEnd = highStart + idxCount;
		
		for (long high = highStart; high < highEnd; high++)
        {
            long seed = (high << 17) | low;

            bool isSeed = true;
            for (int n = 0; n < 16; n++)
            {
                seed = (seed * multiplier + increment) & mask;
                int next = (int)((seed >> 17) % 62);

                if (next != localSeq[n])
                {
                    isSeed = false;
                    break;
                }
            }

            if (isSeed)
            {
                //Add current seed (start seed is (high << 17) | low)
				seeds[0] = seed;
            }
        }
	}
}