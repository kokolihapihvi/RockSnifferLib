#define EXPORT __attribute__((visibility("default")))
#include <mach/mach.h>
#include <unistd.h>
#include <mach/mach_vm.h>
#include <mach/vm_region.h>
#include <mach/vm_map.h>
#include <stdio.h>
#include <mach-o/loader.h>
#include <libproc.h>
#include <sys/proc_info.h>
#include <stdlib.h>

kern_return_t
find_main_binary(pid_t pid, mach_vm_address_t *main_address);

EXPORT int vm_read_wrapper(
    unsigned int target_task,
    unsigned long address,
    unsigned long size,
    unsigned long *data,
    unsigned int *dataCnt)
{
    return vm_read(target_task, address, size, data, dataCnt);
}

EXPORT int vm_deallocate_wrapper(
    vm_map_t target_task,
    vm_address_t address,
    vm_size_t size)
{
    return vm_deallocate(target_task, address, size);
}

EXPORT int task_for_pid_wrapper(unsigned int process, unsigned int *tout)
{
    return task_for_pid(current_task(), process, tout);
}

EXPORT int find_main_binary_wrapper(unsigned int pid, unsigned long long *vmoffset)
{
    int ret = find_main_binary(pid, vmoffset);
    //printf("[dylib] ret: %d vmoffset1: %p \n", ret, vmoffset);
    fflush(stdout);
    return ret;
}

EXPORT int mach_vm_region_recurse_wrapper(vm_map_t target_task, mach_vm_address_t *addr,
                                          mach_vm_size_t *lsize, unsigned int *user_tag)
{
    int success = 1;
    mach_msg_type_number_t infoCount;
    natural_t depth = 0;
    vm_region_submap_info_data_64_t regionInfo;

    while (1)
    {
        infoCount = VM_REGION_SUBMAP_INFO_COUNT_64;
        success = success && mach_vm_region_recurse(target_task, addr, lsize, &depth, (vm_region_recurse_info_t)&regionInfo, &infoCount) == KERN_SUCCESS;
        if (success)
        {
            *user_tag = regionInfo.user_tag;
        }
        if (!success || !regionInfo.is_submap)
            break;
        depth++;
    }
    return success;
}

EXPORT int mach_vm_region_wrapper(vm_map_t target_task, mach_vm_address_t *addr, mach_vm_size_t *lsize, int *protection)
{
    vm_region_basic_info_data_64_t info;
    mach_msg_type_number_t infoCount;
    mach_port_t objectName = MACH_PORT_NULL;
    infoCount = VM_REGION_BASIC_INFO_COUNT_64;

    int ret = mach_vm_region(target_task, addr, lsize, VM_REGION_BASIC_INFO_64, (vm_region_info_t)&info, &infoCount, &objectName);
    *protection = info.protection;

    return ret;
}

EXPORT void free_wrapper(void *ptr)
{
    if (ptr)
    {
        free(ptr);
    }
}

EXPORT char **proc_pidinfo_wrapper(unsigned int pid, int *numPaths)
{

    // Figure out the size of the buffer needed to hold the list of open FDs
    int bufferSize = proc_pidinfo(pid, PROC_PIDLISTFDS, 0, 0, 0);
    if (bufferSize == -1)
        return NULL;

    // Get the list of open FDs
    struct proc_fdinfo *procFDInfo = (struct proc_fdinfo *)malloc(bufferSize);
    if (!procFDInfo)
        return NULL;

    proc_pidinfo(pid, PROC_PIDLISTFDS, 0, procFDInfo, bufferSize);
    int numberOfProcFDs = bufferSize / PROC_PIDLISTFD_SIZE;

    char **pathPointers = calloc(sizeof(char *), numberOfProcFDs);
    int j = 0;
    for (int i = 0; i < numberOfProcFDs; i++)
    {
        if (procFDInfo[i].proc_fdtype == PROX_FDTYPE_VNODE)
        {
            // A file is open
            struct vnode_fdinfowithpath vnodeInfo;
            int bytesUsed = proc_pidfdinfo(pid, procFDInfo[i].proc_fd, PROC_PIDFDVNODEPATHINFO, &vnodeInfo, PROC_PIDFDVNODEPATHINFO_SIZE);
            if (bytesUsed == PROC_PIDFDVNODEPATHINFO_SIZE)
                pathPointers[j++] = strdup(vnodeInfo.pvip.vip_path);
        }
    }
    //for (int k = 0; k < j; k++)
    //{
    //    printf("path: %s\n", pathPointers[k]);
    //}
    free(procFDInfo);
    *numPaths = j;
    return pathPointers;
}

EXPORT unsigned long scan_mem(unsigned int target_task,
                              unsigned long address,
                              unsigned long size,
                              unsigned long di,
                              int magic)
{
    unsigned long dataIndex = di;
    mach_vm_size_t curr_bytes_read = 0;
    vm_offset_t vm_memory = (vm_offset_t)malloc(size);
    mach_vm_read_overwrite(target_task, address, size, vm_memory, &curr_bytes_read);
    if (curr_bytes_read == size)
    {
        unsigned long alignment = 4;
        unsigned long endLimit = size - 4;
        while (dataIndex <= endLimit)
        {
            unsigned long numberOfStepsToTake = ((endLimit + alignment - dataIndex) / alignment);
            for (unsigned long stepIndex = 0; stepIndex < numberOfStepsToTake; stepIndex++)
            {
                //int val = BitConverter.ToInt32(bytes, (int)dataIndex);
                int val = *(int *)((char *)vm_memory + dataIndex);
                if (val == magic) /* magic number */
                {
                    fflush(stdout);
                    return dataIndex;
                }
                dataIndex += alignment;
            }
        }
    }
    free((void *)vm_memory);
    return 0;
}

EXPORT int proc_pidfdinfo_wrapper()
{
    return 0; //proc_pidfdinfo();
}
/*
 * find main binary by iterating memory region
 * assumes there's only one binary with filetype == MH_EXECUTE
 */
kern_return_t
find_main_binary(pid_t pid, mach_vm_address_t *main_address)
{
    // get task for pid
    vm_map_t target_task = 0;
    kern_return_t kr;
    if (task_for_pid(mach_task_self(), pid, &target_task))
    {
        return KERN_FAILURE;
    }

    vm_address_t iter = 0;
    while (1)
    {
        struct mach_header mh = {0};
        vm_address_t addr = iter;
        vm_size_t lsize = 0;
        uint32_t depth;
        mach_vm_size_t bytes_read = 0;
        struct vm_region_submap_info_64 info;
        mach_msg_type_number_t count = VM_REGION_SUBMAP_INFO_COUNT_64;
        if (vm_region_recurse_64(target_task, &addr, &lsize, &depth, (vm_region_info_t)&info, &count))
        {
            break;
        }
        kr = mach_vm_read_overwrite(target_task, (mach_vm_address_t)addr, (mach_vm_size_t)sizeof(struct mach_header), (mach_vm_address_t)&mh, &bytes_read);
        if (kr == KERN_SUCCESS && bytes_read == sizeof(struct mach_header))
        {
            /* only one image with MH_EXECUTE filetype */
            if ((mh.magic == MH_MAGIC || mh.magic == MH_MAGIC_64) && mh.filetype == MH_EXECUTE)
            {
                //fprintf(stdout, "[dylib][DEBUG] Found main binary mach-o image @ %p!\n", (void *)addr);
                *main_address = addr;
                return KERN_SUCCESS;
            }
        }
        iter = addr + lsize;
    }
    return KERN_FAILURE;
}
