using OpenCL.Net;

using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;

namespace lichess_crack
{
    public static class Gpu
    {
        private static bool initialized = false;
        private static Device device;
        private static Context context;

        private static Program? crackHighProgram;
        private static Kernel? crackHighKernel;

        private static void ErrorCheck(ErrorCode err, string name)
        {
            if (err != ErrorCode.Success)
            {
                Debug.WriteLine("Error: " + name + " (" + err.ToString() + ")");
            }
        }

        private static void ContextNotify(string errInfo, byte[] data, IntPtr cb, IntPtr userData)
        {
            Console.WriteLine("OpenCL Notification: " + errInfo);
        }

        public static bool Init()
        {
            if (initialized)
            {
                return true;
            }

            initialized = true;
            ErrorCode error;
            Platform[] platforms = Cl.GetPlatformIDs(out error);
            ErrorCheck(error, "Cl.GetPlatformIDs");

            List<Device> devicesList = new List<Device>();
            foreach (Platform platform in platforms)
            {
                string platformName = Cl.GetPlatformInfo(platform, PlatformInfo.Name, out error).ToString();
                ErrorCheck(error, "Cl.GetPlatformInfo");

                foreach (Device device in Cl.GetDeviceIDs(platform, DeviceType.Gpu, out error))
                {
                    devicesList.Add(device);
                }
            }

            if (devicesList.Count == 0)
            {
                return false;
            }


            device = devicesList[0];

            //Second parameter is amount of devices
            context = Cl.CreateContext(null, 1, new[] { device }, ContextNotify, IntPtr.Zero, out error);
            ErrorCheck(error, "Cl.CreateContext");

            string crackHigh = Path.Combine(System.Environment.CurrentDirectory, "CrackHigh.cl");
            LoadKernel(crackHigh, "CrackHigh", out crackHighProgram, out crackHighKernel);
            return true;
        }

        private static void LoadKernel(string file, string name, out Program? program, out Kernel? kernel)
        {
            ErrorCode error;
            if (File.Exists(file))
            {
                string programSource = File.ReadAllText(file);
                program = Cl.CreateProgramWithSource(context, 1, new[] { programSource }, null, out error);
                ErrorCheck(error, "Cl.CreateProgramWithSource");

                //Compile kernel source
                error = Cl.BuildProgram(program.Value, 1, new[] { device }, string.Empty, null, IntPtr.Zero);
                ErrorCheck(error, "Cl.BuildProgram");

                //Check for any compilation errors
                if (Cl.GetProgramBuildInfo(program.Value, device, ProgramBuildInfo.Status, out error).CastTo<BuildStatus>()
                    != BuildStatus.Success)
                {
                    ErrorCheck(error, "Cl.GetProgramBuildInfo");
                    Cl.ReleaseContext(context);

                    Console.WriteLine("Cl.GetProgramBuildInfo != Success");
                    Console.WriteLine(Cl.GetProgramBuildInfo(program.Value, device, ProgramBuildInfo.Log, out error));
                    Console.ReadKey();
                }

                //Create the required kernel (entry function)
                kernel = Cl.CreateKernel(program.Value, name, out error);
                ErrorCheck(error, "Cl.CreateKernel");
            }
            else
            {
                program = null;
                kernel = null;
            }
        }

        public static long CrackHigh(int[] sequence, long low)
        {
            if (!crackHighProgram.HasValue || !crackHighKernel.HasValue || !initialized || sequence.Length != 16)
            {
                return -1;
            }

            ErrorCode error;

            IMem<int> sequence_dev = Cl.CreateBuffer<int>(context, MemFlags.CopyHostPtr | MemFlags.ReadOnly, sequence, out error);
            ErrorCheck(error, "CrackHigh(): Cl.CreateBuffer");

            long[] seeds = new long[1];
            IMem<long> seed_dev = Cl.CreateBuffer<long>(context, MemFlags.CopyHostPtr | MemFlags.WriteOnly, seeds, out error);
            ErrorCheck(error, "InitializeParameters(): Cl.CreateBuffer");

            error = Cl.SetKernelArg(crackHighKernel.Value, 0, sequence_dev);
            ErrorCheck(error, "Cl.SetKernelArg");
            error = Cl.SetKernelArg(crackHighKernel.Value, 1, seed_dev);
            ErrorCheck(error, "Cl.SetKernelArg");
            error = Cl.SetKernelArg(crackHighKernel.Value, 2, (IntPtr)sizeof(long), low);
            ErrorCheck(error, "Cl.SetKernelArg");

            CommandQueue cmdQueue = Cl.CreateCommandQueue(context, device, 0, out error);
            ErrorCheck(error, "Cl.CreateCommandQueue");

            int maxGroupWorkSize = Cl.GetKernelWorkGroupInfo(crackHighKernel.Value, device, KernelWorkGroupInfo.WorkGroupSize, out error).CastTo<int>();
            ErrorCheck(error, "Cl.GetKernelWorkGroupInfo");

            Event e;
            int threads = maxGroupWorkSize * 12;
            IntPtr[] workSize = new IntPtr[] { (IntPtr)threads };
            error = Cl.EnqueueNDRangeKernel(cmdQueue, crackHighKernel.Value, 1, null, workSize, null, 0, null, out e);
            ErrorCheck(error, "Cl.EnqueueNDRangeKernel");

            error = Cl.Finish(cmdQueue);
            ErrorCheck(error, "Cl.Finish");

            long[] seed_host = new long[1];
            error = Cl.EnqueueReadBuffer(cmdQueue, seed_dev, Bool.True, (IntPtr)0, (IntPtr)(sizeof(long) * 1), seed_host, 0, null, out e);
            ErrorCheck(error, "CL.EnqueueReadBuffer");

            //Dispose your shit
            error = Cl.ReleaseCommandQueue(cmdQueue);
            ErrorCheck(error, "CL.ReleaseCommandQueue");

            error = Cl.ReleaseMemObject(sequence_dev);
            ErrorCheck(error, "CL.ReleaseMemObject");

            error = Cl.ReleaseMemObject(seed_dev);
            ErrorCheck(error, "CL.ReleaseMemObject");
            return seed_host[0];
        }

        public static void Dispose()
        {
            ErrorCode error = Cl.ReleaseContext(context);
            ErrorCheck(error, "Cl.ReleaseContext");

            if (crackHighKernel.HasValue)
            {
                error = Cl.ReleaseKernel(crackHighKernel.Value);
                ErrorCheck(error, "Cl.ReleaseKernel");
            }

            if (crackHighProgram.HasValue)
            {
                crackHighProgram.Value.Dispose();
            }
        }
    }
}
