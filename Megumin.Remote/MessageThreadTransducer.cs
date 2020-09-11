﻿using Megumin.Remote;
using Megumin.Remote;
using Net.Remote;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MessageQueue = System.Collections.Concurrent.ConcurrentQueue<Megumin.Remote.WorkRequest>;
using MessageQueue2 = System.Collections.Concurrent.ConcurrentQueue<Megumin.Remote.WorkRequest2>;

namespace Megumin.Remote
{
    /// <summary>
    /// object消息 消费者接口
    /// </summary>
    public interface IObjectMessageReceiver
    {
        /// <summary>
        /// 处理消息实例,并返回一个可等待结果
        /// </summary>
        /// <param name="rpcID"></param>
        /// <param name="cmd"></param>
        /// <param name="messageID"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        ValueTask<object> Deal(int rpcID, short cmd, int messageID, object message);
    }

    /// <summary>
    /// object消息 消费者接口
    /// </summary>
    public interface IObjectMessageReceiver2
    {
        /// <summary>
        /// 处理消息实例
        /// </summary>
        /// <param name="rpcID"></param>
        /// <param name="cmd"></param>
        /// <param name="messageID"></param>
        /// <param name="message"></param>
        void Deal(int rpcID, short cmd, int messageID, object message);
    }

    internal struct WorkRequest
    {
        readonly int rpcID;
        readonly short cmd;
        readonly int messageID;
        readonly MiniTask<object> task;
        readonly object message;
        readonly IObjectMessageReceiver r;

        internal WorkRequest(MiniTask<object> task, int rpcID, short cmd, int messageID,
            object message, IObjectMessageReceiver r)
        {
            this.rpcID = rpcID;
            this.cmd = cmd;
            this.messageID = messageID;
            this.task = task;
            this.message = message;
            this.r = r;
        }

        public async void Invoke()
        {
            if (this.task == null)
            {
                return;
            }
            ///此处可以忽略异常处理
            ///
            var response = await r.Deal(rpcID, cmd, messageID, message);

            if (response is Task<object> task)
            {
                response = await task;
            }

            if (response is ValueTask<object> vtask)
            {
                response = await vtask;
            }

            this.task.SetResult(response);
        }
    }

    internal struct WorkRequest2
    {
        readonly int rpcID;
        readonly short cmd;
        readonly int messageID;
        readonly MiniTask<object> task;
        readonly object message;
        readonly IObjectMessageReceiver2 r;

        internal WorkRequest2(MiniTask<object> task, int rpcID, short cmd, int messageID,
            object message, IObjectMessageReceiver2 r)
        {
            this.rpcID = rpcID;
            this.cmd = cmd;
            this.messageID = messageID;
            this.task = task;
            this.message = message;
            this.r = r;
        }

        public void Invoke()
        {
            if (this.task == null)
            {
                return;
            }

            r.Deal(rpcID, cmd, messageID, message);
        }
    }
    /// <summary>
    /// 接收消息池
    /// </summary>
    public partial class MessageThreadTransducer
    {
        static MessageQueue receivePool = new MessageQueue();
        static MessageQueue2 receivePool2 = new MessageQueue2();
        /// <summary>
        /// 在控制执行顺序的线程中刷新，所有异步方法的后续部分都在这个方法中执行
        /// </summary>
        /// <param name="delta"></param>
        public static void Update(double delta)
        {
            while (receivePool.TryDequeue(out var res))
            {
                res.Invoke();
            }

            while (receivePool2.TryDequeue(out var res))
            {
                res.Invoke();
            }

            while (actions.TryDequeue(out var callback))
            {
                callback?.Invoke();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static IMiniAwaitable<object> Push(int rpcID, short cmd, int messageID, object message, IObjectMessageReceiver r)
        {
            //这里是性能敏感区域，使用结构体优化，不使用action闭包
            MiniTask<object> task = MiniTask<object>.Rent();
            WorkRequest work = new WorkRequest(task, rpcID, cmd, messageID, message, r);
            receivePool.Enqueue(work);
            return task;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Push2(int rpcID, short cmd, int messageID, object message, IObjectMessageReceiver2 r)
        {
            //这里是性能敏感区域，使用结构体优化，不使用action闭包
            MiniTask<object> task = MiniTask<object>.Rent();
            WorkRequest2 work = new WorkRequest2(task, rpcID, cmd, messageID, message, r);
            receivePool2.Enqueue(work);
        }

        static readonly ConcurrentQueue<Action> actions = new ConcurrentQueue<Action>();

        /// <summary>
        /// 切换执行线程
        /// </summary>
        /// <param name="action"></param>
        public static void Invoke(Action action)
        {
            actions.Enqueue(action);
        }
    }
}
